using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using TilePlanner.Core.Services;

namespace TilePlanner.Core
{
    // ==========================================
    // [V3.1 終極防護] 全域警告吞噬者 (Warning Swallower)
    // ==========================================
    public class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor f in failures)
            {
                // 只要是警告 (如：零件未相交)，一律靜默刪除
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(f);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }

    public class TilePartEngine
    {
        private readonly Document _doc;
        private readonly TileConfig _config;
        private readonly RevitGridService _gridService;
        private readonly DivisionService _divisionService;
        private readonly OpeningService _openingService;

        public TilePartEngine(Document doc, TileConfig config)
        {
            _doc = doc;
            _config = config;
            _gridService = new RevitGridService(doc);
            _divisionService = new DivisionService(doc);
            _openingService = new OpeningService(doc);
        }

        // [V3.1] 此方法已拔除內部 Transaction，完全依賴外部的 Master Transaction
        public void ExecuteOnElement(Element hostElement)
        {
            if (!(hostElement is Part targetPart)) return;

            // 1. [母體溯源]
            ElementId currentId = targetPart.Id;
            Element currentElement = targetPart;
            while (currentElement is Part p)
            {
                var sourceIds = p.GetSourceElementIds();
                if (sourceIds == null || sourceIds.Count == 0) break;
                currentId = sourceIds.First().HostElementId;
                currentElement = _doc.GetElement(currentId);
            }
            Element hostOriginal = currentElement;
            if (hostOriginal == null || hostOriginal is Part) return; 

            // ==========================================
            // [V3.2 核心修正] 先破壞並刷新，再抓取幾何！
            // 必須先讓牆壁回歸真實尺寸並 Regenerate，才能抓到拉伸後的新 BoundingBox
            // ==========================================
            _gridService.ClearOldGridElements(hostOriginal.Id); 
            
            var rootParts = PartUtils.GetAssociatedParts(_doc, hostOriginal.Id, false, false);
            var makersToDelete = new HashSet<ElementId>();
            foreach (var rootPartId in rootParts)
            {
                var pm = PartUtils.GetAssociatedPartMaker(_doc, rootPartId);
                if (pm != null) makersToDelete.Add(pm.Id);
            }
            
            if (makersToDelete.Count > 0)
            {
                _doc.Delete(makersToDelete.ToList()); 
                _doc.Regenerate(); // [第一次刷新]：讓牆體瞬間回歸原始狀態與最新尺寸
            }

            // ==========================================
            // 現在牆壁乾淨了，尺寸也是最新的，可以安全抓取表面幾何了
            // ==========================================
            PlanarFace fullHostTargetFace = GetTargetFace(hostOriginal);
            if (fullHostTargetFace == null) return;
            Plane plane = Plane.CreateByOriginAndBasis(fullHostTargetFace.Origin, fullHostTargetFace.XVector, fullHostTargetFace.YVector);

            List<ElementId> siblingPartIds = PartUtils.GetAssociatedParts(_doc, hostOriginal.Id, false, true).ToList();
            SketchPlane sketchPlane = SketchPlane.Create(_doc, plane);
            List<BoundingBoxXYZ> openingBoxes = _openingService.FindLinkedOpenings(hostOriginal);

            List<ElementId> horizPlanes = new List<ElementId>();
            List<ElementId> vertPlanesSetA = new List<ElementId>();
            List<ElementId> vertPlanesSetB = new List<ElementId>();

            // 3. 建立網格與約束 (內含 2mm 邊界保護)
            CreateGridWeb(fullHostTargetFace, horizPlanes, vertPlanesSetA, vertPlanesSetB, hostOriginal.Id);
            _doc.Regenerate(); // [第二次刷新]：讓剛建立的網格生效

            // ==========================================
            // [V3.5 核心修復] 捨棄標註，改用 Group 強制鎖定網格
            // ==========================================
            GridConstraintManager constraintMgr = new GridConstraintManager(_doc);
            
            // 打包水平網格群組
            constraintMgr.GroupPlanes(horizPlanes, "TileGrid_H", hostOriginal.Id.ToString());
            
            // 將 A/B 兩組交丁排網格合併，打包為一個垂直網格群組
            List<ElementId> allVertPlanes = new List<ElementId>(vertPlanesSetA);
            allVertPlanes.AddRange(vertPlanesSetB);
            constraintMgr.GroupPlanes(allVertPlanes, "TileGrid_V", hostOriginal.Id.ToString());
            _doc.Regenerate(); // [第三次刷新]：讓標註鎖定生效

            // 4. 兩階段分割與開口排除
            PerformDivision(siblingPartIds, horizPlanes, vertPlanesSetA, vertPlanesSetB, sketchPlane.Id, fullHostTargetFace.YVector);

            if (openingBoxes.Count > 0) _openingService.ExcludePartsInOpenings(hostOriginal.Id, openingBoxes);
        }

        private PlanarFace GetTargetFace(Element host)
        {
            Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geom = host.get_Geometry(opt);
            PlanarFace target = null;
            double maxArea = 0;
            foreach (GeometryObject obj in geom)
            {
                if (obj is Solid s && s.Faces.Size > 0)
                {
                    foreach (Face f in s.Faces)
                    {
                        if (f is PlanarFace pf && pf.Area > maxArea) { maxArea = pf.Area; target = pf; }
                    }
                }
            }
            return target;
        }

        private void CreateGridWeb(PlanarFace face, List<ElementId> h, List<ElementId> va, List<ElementId> vb, ElementId hostId)
        {
            BoundingBoxUV bb = face.GetBoundingBox();
            XYZ x = face.XVector, y = face.YVector, n = face.FaceNormal, o = face.Origin;
            double ud = _config.CellWidthFeet, vd = _config.CellHeightFeet;
            
            double ext = 300.0 / 304.8; 
            double edgeTolerance = 0.0065; 

            Category sc = _gridService.GetOrCreateSubcategory();

            var hPoints = GeometryService.CalculateGridPoints(bb.Min.V, bb.Max.V, vd, _config.HGroutGapFeet / 2.0);
            hPoints.RemoveAll(p => p <= bb.Min.V + edgeTolerance || p >= bb.Max.V - edgeTolerance);

            for (int i = 0; i < hPoints.Count; i++)
            {
                XYZ p1 = GeometryService.ProjectToXYZ(o, x, y, bb.Min.U - ext, hPoints[i]);
                XYZ p2 = GeometryService.ProjectToXYZ(o, x, y, bb.Max.U + ext, hPoints[i]);
                var id = _gridService.CreateReferencePlane(p1, p2, n, $"TileGrid_H_{i}_{hostId}", sc);
                if (id != ElementId.InvalidElementId) h.Add(id);
            }

            var vPoints = GeometryService.CalculateGridPoints(bb.Min.U, bb.Max.U, ud, _config.VGroutGapFeet / 2.0);
            vPoints.RemoveAll(p => p <= bb.Min.U + edgeTolerance || p >= bb.Max.U - edgeTolerance);

            for (int i = 0; i < vPoints.Count; i++)
            {
                XYZ p1_A = GeometryService.ProjectToXYZ(o, x, y, vPoints[i], bb.Min.V - ext);
                XYZ p2_A = GeometryService.ProjectToXYZ(o, x, y, vPoints[i], bb.Max.V + ext);
                var idA = _gridService.CreateReferencePlane(p1_A, p2_A, n, $"TileGrid_VA_{i}_{hostId}", sc);
                if (idA != ElementId.InvalidElementId) va.Add(idA);

                if (_config.PatternType == TilePatternType.RunningBond)
                {
                    double offsetU = vPoints[i] + ud * _config.RunningBondOffset;
                    if (offsetU > bb.Min.U + edgeTolerance && offsetU < bb.Max.U - edgeTolerance)
                    {
                        XYZ p1_B = GeometryService.ProjectToXYZ(o, x, y, offsetU, bb.Min.V - ext);
                        XYZ p2_B = GeometryService.ProjectToXYZ(o, x, y, offsetU, bb.Max.V + ext);
                        var idB = _gridService.CreateReferencePlane(p1_B, p2_B, n, $"TileGrid_VB_{i}_{hostId}", sc);
                        if (idB != ElementId.InvalidElementId) vb.Add(idB);
                    }
                }
            }
        }

        private void PerformDivision(List<ElementId> siblingIds, List<ElementId> hPlanes, List<ElementId> vaPlanes, List<ElementId> vbPlanes, ElementId sketchId, XYZ ySort)
        {
            // 1. 先執行水平切割
            if (hPlanes.Count > 0)
            {
                foreach (var pid in siblingIds)
                {
                    try
                    {
                        PartMaker pmH = _divisionService.Divide(new List<ElementId> { pid }, hPlanes, sketchId);
                        if (pmH != null) _divisionService.SetGroutGap(pmH, _config.HGroutGapFeet);
                    }
                    catch { }
                }
                _doc.Regenerate();
            }

            // 2. 抓取水平切割後產生的所有子零件 (橫條)
            var stripIds = new List<ElementId>();
            foreach (var pid in siblingIds) 
            {
                if (PartUtils.HasAssociatedParts(_doc, pid))
                    stripIds.AddRange(PartUtils.GetAssociatedParts(_doc, pid, false, true));
                else
                    stripIds.Add(pid);
            }
            
            if (stripIds.Count == 0) return;

            // 3. 垂直切割
            if (_config.PatternType == TilePatternType.Grid)
            {
                // 正排：所有橫條都用同一組 VA 網格，不受門窗影響
                if (vaPlanes.Count > 0)
                {
                    foreach (var stripId in stripIds)
                    {
                        try 
                        {
                            PartMaker pmV = _divisionService.Divide(new List<ElementId> { stripId }, vaPlanes, sketchId);
                            if (pmV != null) _divisionService.SetGroutGap(pmV, _config.VGroutGapFeet);
                        } 
                        catch { }
                    }
                }
            }
            else
            {
                // ==========================================
                // [V3.5 核心修正] 交丁排：精準高度群組化 (解決門窗截斷問題)
                // ==========================================
                
                // 將所有橫條零件依據其「中心點高度 (Y/Z軸)」進行排序
                var sortedStrips = stripIds.Select(id => _doc.GetElement(id) as Part)
                    .Where(p => p != null && p.get_BoundingBox(null) != null)
                    .Select(p => new {
                        Part = p,
                        CenterY = ((p.get_BoundingBox(null).Min + p.get_BoundingBox(null).Max) * 0.5).DotProduct(ySort)
                    })
                    .OrderBy(x => x.CenterY)
                    .ToList();

                if (sortedStrips.Count > 0)
                {
                    List<List<Part>> rows = new List<List<Part>>();
                    rows.Add(new List<Part> { sortedStrips[0].Part });
                    double currentLastCenterY = sortedStrips[0].CenterY;

                    // 依據高度差異，將橫條分裝進對應的「列 (Row)」
                    for (int i = 1; i < sortedStrips.Count; i++)
                    {
                        // 如果兩塊零件的高度差異「小於半塊磁磚」，代表它們被門窗切斷，但屬於同一列
                        if (Math.Abs(sortedStrips[i].CenterY - currentLastCenterY) < (_config.CellHeightFeet * 0.5))
                        {
                            rows.Last().Add(sortedStrips[i].Part);
                        }
                        else
                        {
                            // 高度差異超過半塊磁磚，建立新的一列
                            rows.Add(new List<Part> { sortedStrips[i].Part });
                            currentLastCenterY = sortedStrips[i].CenterY;
                        }
                    }

                    // 現在 rows 完美代表了物理上的每一列 (不受門窗干擾)
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var vps = (i % 2 == 0) ? vaPlanes : vbPlanes;
                        if (vps.Count > 0)
                        {
                            foreach (var part in rows[i])
                            {
                                try
                                {
                                    PartMaker pmV = _divisionService.Divide(new List<ElementId> { part.Id }, vps, sketchId);
                                    if (pmV != null) _divisionService.SetGroutGap(pmV, _config.VGroutGapFeet);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
        }
    }
}
