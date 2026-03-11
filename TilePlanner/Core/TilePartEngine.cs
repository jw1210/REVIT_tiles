using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core
{
    public class TilePartEngine
    {
        private readonly Document _doc;
        private readonly TileConfig _config;

        public TilePartEngine(Document doc, TileConfig config)
        {
            _doc = doc;
            _config = config;
        }

        public void ExecuteOnElement(Element hostElement)
        {
            // 第一步：取得宿主與零件
            if (!(hostElement is Part targetPart))
            {
                throw new InvalidOperationException("所選取的物件必須是零件 (Part)。");
            }

            Options opt = new Options { ComputeReferences = true };
            GeometryElement geomElem = targetPart.get_Geometry(opt);
            
            Solid solid = null;
            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid s && s.Faces.Size > 0)
                {
                    solid = s;
                    break;
                }
            }

            if (solid == null) throw new InvalidOperationException("無法解析零件的幾何實體。");

            // Find the largest planar face
            PlanarFace targetFace = null;
            double maxArea = 0;
            
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    if (pf.Area > maxArea)
                    {
                        maxArea = pf.Area;
                        targetFace = pf;
                    }
                }
            }

            if (targetFace == null) throw new InvalidOperationException("找不到足夠面積的平整面來進行磁磚分割。");

            //Setup SketchPlane matching the PlanarFace exactly (used for 2D curve slicing context if needed, but we pass ref planes)
            Plane plane = Plane.CreateByOriginAndBasis(targetFace.Origin, targetFace.XVector, targetFace.YVector);
            
            SketchPlane sketchPlane;
            using (Transaction tSketch = new Transaction(_doc, "Create Sketch Plane"))
            {
                tSketch.Start();
                sketchPlane = SketchPlane.Create(_doc, plane);
                tSketch.Commit();
            }

            // 找尋所有與宿主相交的外參門窗 (Linked Openings)
            List<BoundingBoxXYZ> openingBoxes = new List<BoundingBoxXYZ>();
            var links = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            BoundingBoxXYZ hostBox = hostElement.get_BoundingBox(null);
            if (hostBox != null)
            {
                Outline hostOutline = new Outline(hostBox.Min, hostBox.Max);
                foreach (var link in links)
                {
                    Document linkDoc = link.GetLinkDocument();
                    if (linkDoc == null) continue;

                    var openings = new FilteredElementCollector(linkDoc)
                        .WhereElementIsNotElementType()
                        .WherePasses(new LogicalOrFilter(
                            new ElementCategoryFilter(BuiltInCategory.OST_Windows),
                            new ElementCategoryFilter(BuiltInCategory.OST_Doors)
                        )).ToList();

                    Transform transform = link.GetTransform();
                    foreach (var op in openings)
                    {
                        BoundingBoxXYZ opBox = op.get_BoundingBox(null);
                        if (opBox != null)
                        {
                            XYZ p1 = transform.OfPoint(opBox.Min);
                            XYZ p2 = transform.OfPoint(opBox.Max);
                            
                            XYZ trueMin = new XYZ(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Min(p1.Z, p2.Z));
                            XYZ trueMax = new XYZ(Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y), Math.Max(p1.Z, p2.Z));
                            
                            Outline opOutline = new Outline(trueMin, trueMax);
                            
                            // 加上誤差容許避免邊界剛好切齊被過濾掉
                            if (hostOutline.Intersects(opOutline, 0.5))
                            {
                                openingBoxes.Add(new BoundingBoxXYZ { Min = trueMin, Max = trueMax });
                            }
                        }
                    }
                }
            }

            // 第二步：運算與生成鎖定刀網 (Math & Locked Planes)
            // 分拆出水平面、垂直A面、垂直B面
            List<ElementId> horizPlanes = new List<ElementId>();
            List<ElementId> vertPlanesSetA = new List<ElementId>();
            List<ElementId> vertPlanesSetB = new List<ElementId>();

            using (Transaction tPlanes = new Transaction(_doc, "Create Reference Planes"))
            {
                tPlanes.Start();
                CreateReferencePlanes(targetFace, horizPlanes, vertPlanesSetA, vertPlanesSetB, hostElement.Id, openingBoxes);
                tPlanes.Commit();
            }

            if (horizPlanes.Count == 0 || vertPlanesSetA.Count == 0) return;

            // 第三步：階段 A - 水平切條 (Horizontal Striping)
            using (Transaction tDivH = new Transaction(_doc, "Horizontal Divide Parts"))
            {
                tDivH.Start();
                PartUtils.DivideParts(
                    _doc,
                    new List<ElementId> { targetPart.Id },
                    horizPlanes,
                    new List<Curve>(),
                    sketchPlane.Id
                );
                tDivH.Commit();
            }
            
            // 重要：Regenerate 使切割後的實體 (Parts) 真實生成在文件中
            _doc.Regenerate();

            // 尋找剛剛切出來的帶狀子零件
            // 使用 false, true 來排除母體，只取最新生成的子零件 (strips)
            ICollection<ElementId> stripIds = PartUtils.GetAssociatedParts(_doc, targetPart.Id, false, true);
            
            if (stripIds.Count <= 1)
            {
                // 如果沒有成功切出多餘一條，或者因為設定間距過大只有一條，直接以單排處理
            }

            // 第四步：階段 B - 高度分類與垂直切割 (Z-Axis Sorting & Slicing)
            var stripParts = new List<Part>();
            foreach (var id in stripIds)
            {
                if (_doc.GetElement(id) is Part p)
                {
                    // 確保這些條狀零件是可以繼續切割的 (排除母體或無法切割者)
                    if (PartUtils.ArePartsValidForDivide(_doc, new List<ElementId> { p.Id }))
                    {
                        stripParts.Add(p);
                    }
                }
            }

            // 依據高度 (Z 軸或局部 V 軸中心點) 排序
            // 注意：這裡假設牆面垂直，直接抓 BoundingBox Min.Z，若是傾斜面或樓板要抓對應高度
            var sortedStrips = stripParts.OrderBy(p => p.get_BoundingBox(null).Min.Z).ToList();

            using (Transaction tDivV = new Transaction(_doc, "Vertical Divide Parts"))
            {
                tDivV.Start();

                for (int i = 0; i < sortedStrips.Count; i++)
                {
                    bool isEvenRow = (i % 2 == 0);
                    List<ElementId> verticalPlanesToUse = isEvenRow ? vertPlanesSetA : vertPlanesSetB;

                    if (verticalPlanesToUse.Count > 0)
                    {
                        PartUtils.DivideParts(
                            _doc,
                            new List<ElementId> { sortedStrips[i].Id },
                            verticalPlanesToUse,
                            new List<Curve>(),
                            sketchPlane.Id
                        );
                    }
                }

                tDivV.Commit();
            }

            // 第五步：自動排除 (Auto-Exclusion) 灰縫與外參開口廢料
            // 需要找到所有最終的 Parts (排除母體，只拿真正的最新葉節點)
            ICollection<ElementId> finalPartIds = PartUtils.GetAssociatedParts(_doc, targetPart.Id, false, true);
            List<ElementId> partsToExclude = new List<ElementId>();
            
            foreach (ElementId pId in finalPartIds)
            {
                if (_doc.GetElement(pId) is Part p)
                {
                    bool exclude = false;

                    // 1. 檢查是否落入外參開口 (中心點判定)
                    BoundingBoxXYZ pBox = p.get_BoundingBox(null);
                    if (pBox != null && openingBoxes.Count > 0)
                    {
                        XYZ center = (pBox.Min + pBox.Max) * 0.5;
                        foreach (var opBox in openingBoxes)
                        {
                            if (center.X >= opBox.Min.X && center.X <= opBox.Max.X &&
                                center.Y >= opBox.Min.Y && center.Y <= opBox.Max.Y &&
                                center.Z >= opBox.Min.Z && center.Z <= opBox.Max.Z)
                            {
                                exclude = true;
                                break;
                            }
                        }
                    }

                    // 2. 檢查是否為灰縫 (尺寸判定)
                    if (!exclude)
                    {
                        // 取得這個 Part 與 targetFace 平行的面來計算寬高
                        GeometryElement geom = p.get_Geometry(new Options { ComputeReferences = true });
                        PlanarFace parallelFace = null;
                        if (geom != null)
                        {
                            foreach (GeometryObject go in geom)
                            {
                                if (go is Solid s)
                                {
                                    foreach (Face f in s.Faces)
                                    {
                                        if (f is PlanarFace pf && pf.FaceNormal.IsAlmostEqualTo(targetFace.FaceNormal, 0.01))
                                        {
                                            parallelFace = pf;
                                            break;
                                        }
                                    }
                                }
                                if (parallelFace != null) break;
                            }
                        }

                        if (parallelFace != null)
                        {
                            BoundingBoxUV uvBox = parallelFace.GetBoundingBox();
                            double w = uvBox.Max.U - uvBox.Min.U;
                            double h = uvBox.Max.V - uvBox.Min.V;
                            double gw = _config.GroutWidth;
                            
                            // 加上誤差容許值，任一軸小等於灰縫寬度即判定為灰縫廢料
                            if (w <= gw + 0.05 || h <= gw + 0.05)
                            {
                                exclude = true;
                            }
                        }
                    }

                    if (exclude)
                    {
                        partsToExclude.Add(p.Id);
                    }
                }
            }

            if (partsToExclude.Count > 0)
            {
                using (Transaction tExclude = new Transaction(_doc, "Exclude Grout and Openings"))
                {
                    tExclude.Start();
                    // 在 Revit API 中，隱藏 Part 常用 DPART_EXCLUDED 或直接呼叫功能
                    foreach (ElementId exId in partsToExclude)
                    {
                        Part pt = _doc.GetElement(exId) as Part;
                        if (pt != null)
                        {
                            Parameter excludedParam = pt.get_Parameter(BuiltInParameter.DPART_EXCLUDED);
                            if (excludedParam != null && !excludedParam.IsReadOnly)
                            {
                                excludedParam.Set(1);
                            }
                        }
                    }
                    tExclude.Commit();
                }
            }
        }

        private void CreateReferencePlanes(PlanarFace face, List<ElementId> horizPlanes, List<ElementId> vertPlanesSetA, List<ElementId> vertPlanesSetB, ElementId hostId, List<BoundingBoxXYZ> openingBoxes)
        {
            BoundingBoxUV bbox = face.GetBoundingBox();

            double widthUV = bbox.Max.U - bbox.Min.U;
            double heightUV = bbox.Max.V - bbox.Min.V;

            double uDist = _config.CellWidthFeet;
            double vDist = _config.CellHeightFeet;
            double gDist = _config.GroutWidth;
            
            List<ElementId> allRefPlanes = new List<ElementId>();
            string hostSuffix = hostId.IntegerValue.ToString();

            // 1. 生成水平雙切線 (Double-Blade)
            int numHRows = (int)Math.Ceiling(heightUV / vDist) + 1;
            for (int i = 1; i <= numHRows; i++)
            {
                double vPos1 = bbox.Min.V + i * vDist;
                double vPos2 = vPos1 - gDist; // 灰縫的下緣
                
                if (vPos1 <= bbox.Max.V + 1.0)
                {
                    XYZ p1 = face.Evaluate(new UV(bbox.Min.U - 100.0, vPos1));
                    XYZ p2 = face.Evaluate(new UV(bbox.Max.U + 100.0, vPos1));
                    XYZ cutDir = p2 - p1;
                    XYZ cutNorm = cutDir.CrossProduct(face.FaceNormal).Normalize();
                    var rp = _doc.Create.NewReferencePlane(p1, p2, cutNorm, _doc.ActiveView);
                    if (rp != null) { rp.Name = $"TileGrid_H_{i}_{hostSuffix}"; horizPlanes.Add(rp.Id); allRefPlanes.Add(rp.Id); }
                }
                
                if (vPos2 <= bbox.Max.V + 1.0 && vPos2 >= bbox.Min.V - 0.1)
                {
                    XYZ p1 = face.Evaluate(new UV(bbox.Min.U - 100.0, vPos2));
                    XYZ p2 = face.Evaluate(new UV(bbox.Max.U + 100.0, vPos2));
                    XYZ cutDir = p2 - p1;
                    XYZ cutNorm = cutDir.CrossProduct(face.FaceNormal).Normalize();
                    var rp = _doc.Create.NewReferencePlane(p1, p2, cutNorm, _doc.ActiveView);
                    if (rp != null) { rp.Name = $"TileGrid_H_G_{i}_{hostSuffix}"; horizPlanes.Add(rp.Id); allRefPlanes.Add(rp.Id); }
                }
            }

            // 2. 生成垂直雙切線 (Double-Blade)
            int numCols = (int)Math.Ceiling(widthUV / uDist) + 2;
            
            Action<double, string, List<ElementId>> addVerticalDualCuts = (uOrigin, prefix, list) =>
            {
                double uPos1 = uOrigin;
                double uPos2 = uPos1 - gDist; // 灰縫的左緣

                if (uPos1 >= bbox.Min.U - 1.0 && uPos1 <= bbox.Max.U + 1.0)
                {
                    XYZ p1 = face.Evaluate(new UV(uPos1, bbox.Min.V - 100.0));
                    XYZ p2 = face.Evaluate(new UV(uPos1, bbox.Max.V + 100.0));
                    XYZ cutDir = p2 - p1;
                    XYZ cutNorm = cutDir.CrossProduct(face.FaceNormal).Normalize();
                    var rp = _doc.Create.NewReferencePlane(p1, p2, cutNorm, _doc.ActiveView);
                    if (rp != null) { rp.Name = $"{prefix}_{hostSuffix}"; list.Add(rp.Id); allRefPlanes.Add(rp.Id); }
                }

                if (uPos2 >= bbox.Min.U - 1.0 && uPos2 <= bbox.Max.U + 1.0)
                {
                    XYZ p1 = face.Evaluate(new UV(uPos2, bbox.Min.V - 100.0));
                    XYZ p2 = face.Evaluate(new UV(uPos2, bbox.Max.V + 100.0));
                    XYZ cutDir = p2 - p1;
                    XYZ cutNorm = cutDir.CrossProduct(face.FaceNormal).Normalize();
                    var rp = _doc.Create.NewReferencePlane(p1, p2, cutNorm, _doc.ActiveView);
                    if (rp != null) { rp.Name = $"{prefix}_G_{hostSuffix}"; list.Add(rp.Id); allRefPlanes.Add(rp.Id); }
                }
            };

            for (int i = -1; i <= numCols; i++)
            {
                addVerticalDualCuts(bbox.Min.U + i * uDist, $"TileGrid_VA_{i}", vertPlanesSetA);
            }

            if (_config.PatternType == TilePatternType.RunningBond)
            {
                double offsetAmount = uDist * _config.RunningBondOffset;
                for (int i = -1; i <= numCols; i++)
                {
                    addVerticalDualCuts(bbox.Min.U + i * uDist + offsetAmount, $"TileGrid_VB_{i}", vertPlanesSetB);
                }
            }
            else
            {
                // 若為 Grid 模式，SetB 等同 SetA
                vertPlanesSetB.AddRange(vertPlanesSetA);
            }

            // 3. 生成外參開口的四週邊界參照平面
            int opIdx = 0;
            foreach (var opBox in openingBoxes)
            {
                // 將開口的 8 個 3D 頂點投影到目標面上，求取 UV 的極值 (BoundingBoxUV)
                List<XYZ> corners = new List<XYZ>
                {
                    new XYZ(opBox.Min.X, opBox.Min.Y, opBox.Min.Z),
                    new XYZ(opBox.Max.X, opBox.Min.Y, opBox.Min.Z),
                    new XYZ(opBox.Min.X, opBox.Max.Y, opBox.Min.Z),
                    new XYZ(opBox.Max.X, opBox.Max.Y, opBox.Min.Z),
                    new XYZ(opBox.Min.X, opBox.Min.Y, opBox.Max.Z),
                    new XYZ(opBox.Max.X, opBox.Min.Y, opBox.Max.Z),
                    new XYZ(opBox.Min.X, opBox.Max.Y, opBox.Max.Z),
                    new XYZ(opBox.Max.X, opBox.Max.Y, opBox.Max.Z),
                };

                double opUMin = double.MaxValue, opUMax = double.MinValue;
                double opVMin = double.MaxValue, opVMax = double.MinValue;

                bool projectedAny = false;
                foreach (var c in corners)
                {
                    IntersectionResult res = face.Project(c);
                    if (res != null)
                    {
                        opUMin = Math.Min(opUMin, res.UVPoint.U);
                        opUMax = Math.Max(opUMax, res.UVPoint.U);
                        opVMin = Math.Min(opVMin, res.UVPoint.V);
                        opVMax = Math.Max(opVMax, res.UVPoint.V);
                        projectedAny = true;
                    }
                }

                if (!projectedAny) continue; // 投影失敗，跳過此開口

                // 生成邊界刀：水平 (上下緣)
                Action<double, string> addOpHoriz = (v, name) => {
                    XYZ p1 = face.Evaluate(new UV(bbox.Min.U - 100.0, v));
                    XYZ p2 = face.Evaluate(new UV(bbox.Max.U + 100.0, v));
                    XYZ cutDir = p2 - p1;
                    XYZ cutNorm = cutDir.CrossProduct(face.FaceNormal).Normalize();
                    var rp = _doc.Create.NewReferencePlane(p1, p2, cutNorm, _doc.ActiveView);
                    if (rp != null) { rp.Name = name; horizPlanes.Add(rp.Id); allRefPlanes.Add(rp.Id); }
                };
                addOpHoriz(opVMin, $"TileGrid_OpH_{opIdx}_Min_{hostSuffix}");
                addOpHoriz(opVMax, $"TileGrid_OpH_{opIdx}_Max_{hostSuffix}");

                // 生成邊界刀：垂直 (左右緣) - 加入 A 與 B 雙集合確保貫穿交丁
                Action<double, string> addOpVert = (u, name) => {
                    XYZ p1 = face.Evaluate(new UV(u, bbox.Min.V - 100.0));
                    XYZ p2 = face.Evaluate(new UV(u, bbox.Max.V + 100.0));
                    XYZ cutDir = p2 - p1;
                    XYZ cutNorm = cutDir.CrossProduct(face.FaceNormal).Normalize();
                    var rp = _doc.Create.NewReferencePlane(p1, p2, cutNorm, _doc.ActiveView);
                    if (rp != null) { 
                        rp.Name = name; 
                        vertPlanesSetA.Add(rp.Id); 
                        if (_config.PatternType == TilePatternType.RunningBond) vertPlanesSetB.Add(rp.Id); 
                        allRefPlanes.Add(rp.Id); 
                    }
                };
                addOpVert(opUMin, $"TileGrid_OpV_{opIdx}_Min_{hostSuffix}");
                addOpVert(opUMax, $"TileGrid_OpV_{opIdx}_Max_{hostSuffix}");

                opIdx++;
            }

            // 將所有 ReferencePlane 加人群組，讓使用者只要點選任一條參照線就可以整體移動網格！
            if (allRefPlanes.Count > 0)
            {
                try
                {
                    Group group = _doc.Create.NewGroup(allRefPlanes);
                    group.GroupType.Name = $"TileGrid_{hostSuffix}_{Guid.NewGuid().ToString().Substring(0, 5)}";
                }
                catch (Exception)
                {
                    // 萬一群組失敗就不群組
                }
            }

            // TODO: (未來擴充功能) 呼叫 NewDimension 上鎖定位移距離
        }
    }
}
