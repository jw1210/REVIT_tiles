using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core
{
    /// <summary>
    /// V2.0 參數化灰縫版 — 單刀補償與參數退縮法
    /// 使用單線網格 + PartMaker DIVIDER_GAP 原生灰縫機制
    /// </summary>
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

            // 找出最大的平面 (PlanarFace)
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

            // 建立與目標面共面的 SketchPlane
            Plane plane = Plane.CreateByOriginAndBasis(targetFace.Origin, targetFace.XVector, targetFace.YVector);

            using (Transaction t = new Transaction(_doc, "執行 AntiGravity 磁磚分割 V2.0"))
            {
                t.Start();

                SketchPlane sketchPlane = SketchPlane.Create(_doc, plane);

                // ===== 偵測外參連結模型中的門窗開口 =====
                List<BoundingBoxXYZ> openingBoxes = FindLinkedOpenings(hostElement);

                // ===== 第二步：生成單刀補償網格 =====
                List<ElementId> horizPlanes = new List<ElementId>();
                List<ElementId> vertPlanesSetA = new List<ElementId>();
                List<ElementId> vertPlanesSetB = new List<ElementId>();

                CreateSingleBladeGrid(targetFace, horizPlanes, vertPlanesSetA, vertPlanesSetB, hostElement.Id, openingBoxes);

                if (horizPlanes.Count == 0 && vertPlanesSetA.Count == 0)
                {
                    t.RollBack();
                    return;
                }

                // Regenerate 確保 Revit 認知到剛畫好的參照平面
                _doc.Regenerate();

                // ===== V2.3 演算法分流 (Logic Branching) =====
                if (_config.PatternType == TilePatternType.Grid)
                {
                    // 若選擇「正排」： 取消兩階段切割。直接將水平與垂直參照平面陣列「一次性」餵給 DivideParts
                    List<ElementId> allPlanes = new List<ElementId>();
                    allPlanes.AddRange(horizPlanes);
                    allPlanes.AddRange(vertPlanesSetA);

                    if (allPlanes.Count > 0)
                    {
                        PartUtils.DivideParts(
                            _doc,
                            new List<ElementId> { targetPart.Id },
                            allPlanes,
                            new List<Curve>(),
                            sketchPlane.Id
                        );

                        _doc.Regenerate();

                        // 寫入唯一的 PartMaker (若兩向設定不同，以水平為優先，因 Revit 原生限制單一 PartMaker 只能有一種 Gap)
                        SetPartMakerDividerGap(targetPart.Id, _config.HGroutGapFeet);
                    }
                }
                else
                {
                    // 若選擇「交丁」： 啟動兩階段切割
                    // ===== 第三步：水平切條 (階段 A) =====
                    if (horizPlanes.Count > 0)
                    {
                        PartUtils.DivideParts(
                            _doc,
                            new List<ElementId> { targetPart.Id },
                            horizPlanes,
                            new List<Curve>(),
                            sketchPlane.Id
                        );

                        _doc.Regenerate();

                        // 設定水平切割的 PartMaker 灰縫參數 (階段 A：水平)
                        SetPartMakerDividerGap(targetPart.Id, _config.HGroutGapFeet);
                    }

                    // ===== 第四步：垂直切割 (階段 B) =====
                    ICollection<ElementId> stripIds = PartUtils.GetAssociatedParts(_doc, targetPart.Id, false, true);

                    var stripParts = new List<Part>();
                    foreach (var id in stripIds)
                    {
                        if (_doc.GetElement(id) is Part p)
                        {
                            if (PartUtils.ArePartsValidForDivide(_doc, new List<ElementId> { p.Id }))
                            {
                                stripParts.Add(p);
                            }
                        }
                    }

                    // 依高度排序
                    var sortedStrips = stripParts
                        .Where(p => p.get_BoundingBox(null) != null)
                        .OrderBy(p => p.get_BoundingBox(null).Min.Z)
                        .ToList();

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

                    _doc.Regenerate();

                    // 設定垂直切割的 PartMaker 灰縫參數 (階段 B：垂直)
                    foreach (var strip in sortedStrips)
                    {
                        SetPartMakerDividerGap(strip.Id, _config.VGroutGapFeet);
                    }
                }

                // ===== 第五步：開口零件排除 =====
                if (openingBoxes.Count > 0)
                {
                    ExcludeOpeningParts(targetPart.Id, openingBoxes);
                }

                // ===== 第六步：強制視圖狀態更新 (V2.3) =====
                try
                {
                    // 強制切換 Parts Visibility 為「展示零件」
                    _doc.ActiveView.PartsVisibility = PartsVisibility.ShowPartsOnly;

                    Category refPlaneCat = Category.GetCategory(_doc, BuiltInCategory.OST_CLines); 
                    if (refPlaneCat == null) refPlaneCat = Category.GetCategory(_doc, BuiltInCategory.OST_ReferenceLines);
                    
                    if (refPlaneCat != null)
                    {
                        _doc.ActiveView.SetCategoryHidden(refPlaneCat.Id, false);
                    }

                    Category subCat = GetOrCreateSubcategory();
                    if (subCat != null)
                    {
                        _doc.ActiveView.SetCategoryHidden(subCat.Id, false);
                    }
                }
                catch { /* 若視圖屬性被樣板鎖定則忽略 */ }

                t.Commit();
            }
        }

        // =========================================================================
        //  V2.1 單刀補償網格生成 — 面局部座標系 (Face Local Coordinate System)
        // =========================================================================
        private void CreateSingleBladeGrid(
            PlanarFace face,
            List<ElementId> horizPlanes,
            List<ElementId> vertPlanesSetA,
            List<ElementId> vertPlanesSetB,
            ElementId hostId,
            List<BoundingBoxXYZ> openingBoxes)
        {
            if (face == null) return;

            // ===== 面局部座標系 (LCS) =====
            XYZ origin = face.Origin;       // 面上的已知參考點 (世界座標)
            XYZ xDir = face.XVector;         // 面的局部 X 軸方向 (單位向量)
            XYZ yDir = face.YVector;         // 面的局部 Y 軸方向 (單位向量)
            XYZ normal = face.FaceNormal;    // 面的法向量 (切割深度方向)

            // 取得面的 UV 邊界 (局部參數空間)
            BoundingBoxUV bbox = face.GetBoundingBox();
            double widthUV = bbox.Max.U - bbox.Min.U;
            double heightUV = bbox.Max.V - bbox.Min.V;

            // 網格間距 = 磁磚真實尺寸 + 灰縫
            double uDist = _config.CellWidthFeet;
            double vDist = _config.CellHeightFeet;

            // 刀刃安全延伸量：加大至 10000mm (約32.8英呎)，防止使用者將參考線大幅拖曳後失效
            double ext = 10000.0 / 304.8;

            List<ElementId> allRefPlanes = new List<ElementId>();
            string hostSuffix = hostId.IntegerValue.ToString();

            // 建立子品類 (Subcategory) 以便管理
            Category subCat = GetOrCreateSubcategory();

            // --- 輔助方法：使用面局部座標系計算 3D 世界座標 ---
            // point3D = origin + u * xDir + v * yDir
            Func<double, double, XYZ> toWorld = (u, v) =>
                origin + u * xDir + v * yDir;

            // --- 輔助 Lambda：生成單條參照平面 (cutVec = 面法向量) ---
            Func<XYZ, XYZ, string, ElementId> createRefPlane = (p1, p2, name) =>
            {
                XYZ lineDir = p2 - p1;
                if (lineDir.GetLength() < 1e-6) return ElementId.InvalidElementId;

                // 關鍵修正：cutVec 必須等於面法向量，確保切割刀垂直砍入牆體
                var rp = _doc.Create.NewReferencePlane(p1, p2, normal, _doc.ActiveView);
                if (rp != null)
                {
                    rp.Name = name;
                    allRefPlanes.Add(rp.Id);
                    if (subCat != null)
                    {
                        try 
                        { 
                            Parameter subParam = rp.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                            if (subParam == null)
                            {
                                foreach (Parameter p in rp.Parameters)
                                {
                                    string pName = p.Definition.Name.ToLower();
                                    if (p.StorageType == StorageType.ElementId && 
                                        (pName.Contains("subcategory") || pName.Contains("品類") || pName.Contains("子分類")))
                                    {
                                        subParam = p;
                                        break; // 找到第一個 ElementId 且名稱類似 Subcategory 的就綁定
                                    }
                                }
                            }
                            if (subParam != null && !subParam.IsReadOnly)
                            {
                                subParam.Set(subCat.Id);
                            }
                        }
                        catch { /* 忽略異常 */ }
                    }
                    return rp.Id;
                }
                return ElementId.InvalidElementId;
            };

            // ===== 1. 水平單刀 (灰縫中心線) =====
            // 依據延伸量 ext 動態算出充裕的備用迴圈數，防止大範圍偏移造成磁磚無切割
            int extraH = (int)Math.Ceiling(ext / vDist) + 5;
            int numHRows = (int)Math.Ceiling(heightUV / vDist) + extraH;
            for (int i = -extraH; i <= numHRows; i++)
            {
                double vPos = bbox.Min.V + i * vDist;

                if (vPos >= bbox.Min.V - ext && vPos <= bbox.Max.V + ext)
                {
                    // 端點沿 xDir 向兩側延伸 ext，確保完全貫穿零件
                    XYZ p1 = toWorld(bbox.Min.U - ext, vPos);
                    XYZ p2 = toWorld(bbox.Max.U + ext, vPos);
                    ElementId rpId = createRefPlane(p1, p2, $"TileGrid_H_{i}_{hostSuffix}");
                    if (rpId != ElementId.InvalidElementId)
                        horizPlanes.Add(rpId);
                }
            }

            // ===== 2. 垂直單刀 Set A (單數排) =====
            int extraV = (int)Math.Ceiling(ext / uDist) + 5;
            int numCols = (int)Math.Ceiling(widthUV / uDist) + extraV;

            Action<double, string, List<ElementId>> addVerticalCut = (uPos, name, list) =>
            {
                if (uPos >= bbox.Min.U - ext && uPos <= bbox.Max.U + ext)
                {
                    // 端點沿 yDir 向兩側延伸 ext
                    XYZ p1 = toWorld(uPos, bbox.Min.V - ext);
                    XYZ p2 = toWorld(uPos, bbox.Max.V + ext);
                    ElementId rpId = createRefPlane(p1, p2, name);
                    if (rpId != ElementId.InvalidElementId)
                        list.Add(rpId);
                }
            };

            for (int i = -extraV; i <= numCols; i++)
            {
                addVerticalCut(bbox.Min.U + i * uDist, $"TileGrid_VA_{i}_{hostSuffix}", vertPlanesSetA);
            }

            // ===== 3. 垂直單刀 Set B (交丁：Running Bond 模式) =====
            if (_config.PatternType == TilePatternType.RunningBond)
            {
                double offsetAmount = uDist * _config.RunningBondOffset;
                for (int i = -extraV; i <= numCols; i++)
                {
                    addVerticalCut(bbox.Min.U + i * uDist + offsetAmount, $"TileGrid_VB_{i}_{hostSuffix}", vertPlanesSetB);
                }
            }
            else
            {
                // Grid 模式：Set B = Set A
                vertPlanesSetB.AddRange(vertPlanesSetA);
            }

            // ===== 4. 開口邊界刀 =====
            if (openingBoxes != null)
            {
                int opIdx = 0;
                foreach (var opBox in openingBoxes)
                {
                    // 投影開口頂點到目標面取得 UV 極值
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

                    if (!projectedAny) continue;

                    // 水平邊界刀 (上下緣)
                    {
                        XYZ p1 = toWorld(bbox.Min.U - ext, opVMin);
                        XYZ p2 = toWorld(bbox.Max.U + ext, opVMin);
                        ElementId rpId = createRefPlane(p1, p2, $"TileGrid_OpH_{opIdx}_Min_{hostSuffix}");
                        if (rpId != ElementId.InvalidElementId) horizPlanes.Add(rpId);
                    }
                    {
                        XYZ p1 = toWorld(bbox.Min.U - ext, opVMax);
                        XYZ p2 = toWorld(bbox.Max.U + ext, opVMax);
                        ElementId rpId = createRefPlane(p1, p2, $"TileGrid_OpH_{opIdx}_Max_{hostSuffix}");
                        if (rpId != ElementId.InvalidElementId) horizPlanes.Add(rpId);
                    }

                    // 垂直邊界刀 (左右緣) — 同時加入 Set A 與 Set B
                    Action<double, string> addOpVert = (u, name) =>
                    {
                        XYZ p1 = toWorld(u, bbox.Min.V - ext);
                        XYZ p2 = toWorld(u, bbox.Max.V + ext);
                        ElementId rpId = createRefPlane(p1, p2, name);
                        if (rpId != ElementId.InvalidElementId)
                        {
                            vertPlanesSetA.Add(rpId);
                            if (_config.PatternType == TilePatternType.RunningBond)
                                vertPlanesSetB.Add(rpId);
                        }
                    };
                    addOpVert(opUMin, $"TileGrid_OpV_{opIdx}_Min_{hostSuffix}");
                    addOpVert(opUMax, $"TileGrid_OpV_{opIdx}_Max_{hostSuffix}");

                    opIdx++;
                }
            }

            // ===== 5. 尺寸約束鎖定 (Dimension Locking) =====
            // 在建立標註前，必須強制 Regenerate，讓 Revit 正式生成剛建立的參照平面幾何
            // 否則呼叫 rp.GetReference() 丟給 NewDimension 時會拋出異常 (靜默失敗)
            _doc.Regenerate();

            // 將參照平面群建立相對距離標註並設定鎖定，確保微調時同步平移
            Action<List<ElementId>, bool> lockPlanes = (planeIds, isHoriz) =>
            {
                if (planeIds.Count < 2) return;

                // 【修復】依照坐標由小到大排序 (水平刀看 yDir 跨距，垂直刀看 xDir 跨距)
                var sortedIds = planeIds.OrderBy(id => 
                {
                    if (_doc.GetElement(id) is ReferencePlane rp) 
                        return isHoriz ? rp.BubbleEnd.DotProduct(yDir) : rp.BubbleEnd.DotProduct(xDir); 
                    return 0.0;
                }).ToList();

                ReferenceArray refArray = new ReferenceArray();
                foreach (var id in sortedIds)
                {
                    if (_doc.GetElement(id) is ReferencePlane rp)
                    {
                        var rpRef = rp.GetReference();
                        if (rpRef != null) refArray.Append(rpRef);
                    }
                }
                
                if (refArray.Size >= 2)
                {
                    try
                    {
                        // 如果是在 3D 視圖中，維度標註需要有 Active SketchPlane
                        if (_doc.ActiveView is View3D view3D)
                        {
                            try
                            {
                                Plane p = Plane.CreateByNormalAndOrigin(face.FaceNormal, face.Origin);
                                SketchPlane sp = SketchPlane.Create(_doc, p);
                                view3D.SketchPlane = sp;
                            }
                            catch { }
                        }

                        // 建立垂直於切割刀的標註線
                        XYZ p1 = isHoriz ? toWorld(bbox.Min.U, bbox.Min.V - ext) : toWorld(bbox.Min.U - ext, bbox.Min.V);
                        XYZ p2 = isHoriz ? toWorld(bbox.Min.U, bbox.Max.V + ext) : toWorld(bbox.Max.U + ext, bbox.Min.V);

                        if (!(_doc.ActiveView is View3D))
                        {
                            // 投影至 2D 視圖平面，確保標註線與視圖完全平行 (例如樓板平面圖)
                            XYZ vNormal = _doc.ActiveView.ViewDirection;
                            XYZ vOrigin = _doc.ActiveView.Origin;
                            p1 = p1 - (p1 - vOrigin).DotProduct(vNormal) * vNormal;
                            p2 = p2 - (p2 - vOrigin).DotProduct(vNormal) * vNormal;
                        }

                        if (p1.DistanceTo(p2) > 0.1)
                        {
                            Line dimLine = Line.CreateBound(p1, p2);
                            
                            Dimension dim = _doc.Create.NewDimension(_doc.ActiveView, dimLine, refArray);
                            if (dim != null)
                            {
                                // ===== 全段落強制上鎖 (V2.3) =====
                                if (dim.Segments.Size > 0)
                                {
                                    foreach (DimensionSegment seg in dim.Segments)
                                    {
                                        seg.IsLocked = true;
                                    }
                                }
                                else
                                {
                                    dim.IsLocked = true;
                                }
                            }
                        }
                    }
                    catch { /* 當前視圖若非平面/立面或不支援標註則忽略 */ }
                }
            };

            // 1. 鎖定水平網格 (防止移動時只改單磚尺寸)
            lockPlanes(horizPlanes, true);

            // 2. 鎖定所有垂直網格 (將 SetA 與 SetB 合併後一起鎖定，確保交丁比例永遠維持)
            List<ElementId> allVertPlanes = new List<ElementId>(vertPlanesSetA);
            allVertPlanes.AddRange(vertPlanesSetB);
            lockPlanes(allVertPlanes, false);
        }

        // =========================================================================
        //  PartMaker 灰縫參數寫入 (PART_MAKER_DIVIDER_GAP) (V2.2 兩階段獨立寫入)
        // =========================================================================
        private void SetPartMakerDividerGap(ElementId sourcePartId, double groutFeet)
        {
            if (groutFeet <= 0) return;

            // 尋找與此零件相關聯的 PartMaker
            var partMakers = new FilteredElementCollector(_doc)
                .OfClass(typeof(PartMaker))
                .Cast<PartMaker>()
                .ToList();

            foreach (var pm in partMakers)
            {
                // 檢查這個 PartMaker 是否管理我們剛切割的零件
                ICollection<ElementId> srcIds = pm.GetSourceElementIds()
                    .Select(lr => lr.HostElementId)
                    .ToList();

                if (srcIds.Contains(sourcePartId))
                {
                    // PartMaker 的「分割縫隙」參數在 Revit API 中以 LookupParameter 取得
                    // 參數名稱可能因語言版本不同：英文 "Divider gap" / 中文 "分割器間距"
                    Parameter gapParam = pm.LookupParameter("Divider gap")
                                      ?? pm.LookupParameter("分割器間距")
                                      ?? pm.LookupParameter("分隔間距");
                    
                    if (gapParam != null && !gapParam.IsReadOnly)
                    {
                        gapParam.Set(groutFeet);
                    }
                    else
                    {
                        // 備案：嘗試遍歷所有參數找到 Double 型態且名稱含 "gap" / "間距" / "間隙" 或任何非唯讀的 Double
                        // (PartMaker 通常只有極少數參數，Divider gap 是唯一能寫入的 Double)
                        foreach (Parameter param in pm.Parameters)
                        {
                            if (param.StorageType == StorageType.Double && !param.IsReadOnly)
                            {
                                string pName = param.Definition.Name.ToLower();
                                if (pName.Contains("gap") || pName.Contains("間距") || pName.Contains("縫隙") || pName.Contains("間隙") || pName.Contains("分割"))
                                {
                                    param.Set(groutFeet);
                                    break;
                                }
                                else if (pName.Contains("divider")) // 也有可能是純英文 divider
                                {
                                    param.Set(groutFeet);
                                    break;
                                }
                                else
                                {
                                    // 若實在找不到符合字眼，直接把找到的第一個可寫入之 Double 設為間距值！
                                    param.Set(groutFeet);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // =========================================================================
        //  外參開口偵測 (Linked Openings Detection)
        // =========================================================================
        private List<BoundingBoxXYZ> FindLinkedOpenings(Element hostElement)
        {
            List<BoundingBoxXYZ> openingBoxes = new List<BoundingBoxXYZ>();

            var links = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            BoundingBoxXYZ hostBox = hostElement.get_BoundingBox(null);
            if (hostBox == null) return openingBoxes;

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
                    if (opBox == null) continue;

                    XYZ p1 = transform.OfPoint(opBox.Min);
                    XYZ p2 = transform.OfPoint(opBox.Max);

                    XYZ trueMin = new XYZ(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Min(p1.Z, p2.Z));
                    XYZ trueMax = new XYZ(Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y), Math.Max(p1.Z, p2.Z));

                    Outline opOutline = new Outline(trueMin, trueMax);

                    if (hostOutline.Intersects(opOutline, 0.5))
                    {
                        openingBoxes.Add(new BoundingBoxXYZ { Min = trueMin, Max = trueMax });
                    }
                }
            }

            return openingBoxes;
        }

        // =========================================================================
        //  開口零件排除 (Opening Parts Exclusion)
        // =========================================================================
        private void ExcludeOpeningParts(ElementId rootPartId, List<BoundingBoxXYZ> openingBoxes)
        {
            ICollection<ElementId> finalPartIds = PartUtils.GetAssociatedParts(_doc, rootPartId, false, true);

            foreach (ElementId pId in finalPartIds)
            {
                if (!(_doc.GetElement(pId) is Part p)) continue;

                BoundingBoxXYZ pBox = p.get_BoundingBox(null);
                if (pBox == null) continue;

                XYZ center = (pBox.Min + pBox.Max) * 0.5;

                foreach (var opBox in openingBoxes)
                {
                    if (center.X >= opBox.Min.X && center.X <= opBox.Max.X &&
                        center.Y >= opBox.Min.Y && center.Y <= opBox.Max.Y &&
                        center.Z >= opBox.Min.Z && center.Z <= opBox.Max.Z)
                    {
                        Parameter excludedParam = p.get_Parameter(BuiltInParameter.DPART_EXCLUDED);
                        if (excludedParam != null && !excludedParam.IsReadOnly)
                        {
                            excludedParam.Set(1);
                        }
                        break;
                    }
                }
            }
        }

        // =========================================================================
        //  子品類管理 (Subcategory Management)
        // =========================================================================
        private Category GetOrCreateSubcategory()
        {
            try
            {
                // V2.3 修正：參照平面應屬於 OST_CLines (Reference Planes)，而不是 OST_ReferenceLines (Reference Lines)
                Category refPlaneCat = Category.GetCategory(_doc, BuiltInCategory.OST_CLines);
                if (refPlaneCat == null) return null;

                if (refPlaneCat.SubCategories.Contains("磁磚計畫刀網"))
                {
                    return refPlaneCat.SubCategories.get_Item("磁磚計畫刀網");
                }
                else
                {
                    Category subCat = _doc.Settings.Categories.NewSubcategory(refPlaneCat, "磁磚計畫刀網");
                    // 建立時賦予預設的鮮明顏色 (綠色)
                    subCat.LineColor = new Color(0, 160, 0);
                    return subCat;
                }
            }
            catch
            {
                return null; // 若子品類建立失敗，不影響主流程
            }
        }
    }
}
