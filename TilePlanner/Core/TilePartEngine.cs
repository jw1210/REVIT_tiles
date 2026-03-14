using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using TilePlanner.Core.Services;

namespace TilePlanner.Core
{
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

        public void ExecuteOnElement(Element hostElement)
        {
            if (!(hostElement is Part targetPart)) return;

            // ==========================================
            // 1. [母體溯源] 確保操作永遠針對最源頭的 Wall/Floor
            // ==========================================
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

            PlanarFace fullHostTargetFace = GetTargetFace(hostOriginal);
            if (fullHostTargetFace == null) return;
            Plane plane = Plane.CreateByOriginAndBasis(fullHostTargetFace.Origin, fullHostTargetFace.XVector, fullHostTargetFace.YVector);

            using (Transaction t = new Transaction(_doc, "AntiGravity Tile V2.7 (Redraw Architecture)"))
            {
                t.Start();
                
                // ==========================================
                // 2. [一鍵重繪機制] 破壞與還原 (支援尺寸自適應)
                // ==========================================
                _gridService.ClearOldGridElements(hostOriginal.Id); // 清除舊刀網與標註
                
                var allAssociatedParts = PartUtils.GetAssociatedParts(_doc, hostOriginal.Id, true, true);
                var makersToDelete = new HashSet<ElementId>();
                foreach (var pId in allAssociatedParts)
                {
                    var pm = PartUtils.GetAssociatedPartMaker(_doc, pId);
                    if (pm != null) makersToDelete.Add(pm.Id);
                }
                if (makersToDelete.Count > 0)
                {
                    _doc.Delete(makersToDelete.ToList()); // 刪除舊分割紀錄
                    _doc.Regenerate(); // 強制刷新，讓牆體回歸「最新拉伸後」的完整尺寸
                }

                List<ElementId> siblingPartIds = PartUtils.GetAssociatedParts(_doc, hostOriginal.Id, false, true).ToList();
                SketchPlane sketchPlane = SketchPlane.Create(_doc, plane);
                List<BoundingBoxXYZ> openingBoxes = _openingService.FindLinkedOpenings(hostOriginal);

                List<ElementId> horizPlanes = new List<ElementId>();
                List<ElementId> vertPlanesSetA = new List<ElementId>();
                List<ElementId> vertPlanesSetB = new List<ElementId>();

                // 3. 依據最新尺寸建立網格 (內含邊界保護)
                CreateGridWeb(fullHostTargetFace, horizPlanes, vertPlanesSetA, vertPlanesSetB, hostOriginal.Id);
                _doc.Regenerate();

                // 4. 重新建立整體連動約束
                GridConstraintManager constraintMgr = new GridConstraintManager(_doc, fullHostTargetFace, _config);
                constraintMgr.LockPlanes(horizPlanes, true);
                
                List<ElementId> allVertPlanes = new List<ElementId>(vertPlanesSetA);
                allVertPlanes.AddRange(vertPlanesSetB);
                constraintMgr.LockPlanes(allVertPlanes, false);
                _doc.Regenerate();

                // 5. 執行分割與開口排除
                PerformDivision(siblingPartIds, horizPlanes, vertPlanesSetA, vertPlanesSetB, sketchPlane.Id, fullHostTargetFace.YVector);

                if (_doc.ActiveView != null) _doc.ActiveView.PartsVisibility = PartsVisibility.ShowPartsOnly;
                if (openingBoxes.Count > 0) _openingService.ExcludePartsInOpenings(hostOriginal.Id, openingBoxes);
                t.Commit();
            }
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
            
            // 視覺整潔：參考線長度僅向兩端各延伸 30 公分 (避免無限延伸與約束崩潰)
            double ext = 300.0 / 304.8; 
            
            // ==========================================
            // 3. [邊界保護容差] Edge Tolerance
            // 約 0.0065 呎 (2mm)，剔除剛好壓在極限邊端度的網格
            // ==========================================
            double edgeTolerance = 0.0065; 

            Category sc = _gridService.GetOrCreateSubcategory();

            // 水平網格 (V 軸) - 保護牆頂/牆底或樓板長度邊緣
            var hPoints = GeometryService.CalculateGridPoints(bb.Min.V, bb.Max.V, vd, _config.HGroutGapFeet / 2.0);
            hPoints.RemoveAll(p => p <= bb.Min.V + edgeTolerance || p >= bb.Max.V - edgeTolerance);

            for (int i = 0; i < hPoints.Count; i++)
            {
                XYZ p1 = GeometryService.ProjectToXYZ(o, x, y, bb.Min.U - ext, hPoints[i]);
                XYZ p2 = GeometryService.ProjectToXYZ(o, x, y, bb.Max.U + ext, hPoints[i]);
                var id = _gridService.CreateReferencePlane(p1, p2, n, $"TileGrid_H_{i}_{hostId}", sc);
                if (id != ElementId.InvalidElementId) h.Add(id);
            }

            // 垂直網格 (U 軸) - 保護牆體左右或樓板寬度邊緣
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
                    
                    // 交丁排的 B 網格同樣需要過濾邊界
                    if (offsetU > bb.Min.U + edgeTolerance && offsetU < bb.Max.U - edgeTolerance)
                    {
                        XYZ p1_B = GeometryService.ProjectToXYZ(o, x, y, offsetU, bb.Min.V - ext);
                        XYZ p2_B = GeometryService.ProjectToXYZ(o, x, y, offsetU, bb.Max.U + ext);
                        var idB = _gridService.CreateReferencePlane(p1_B, p2_B, n, $"TileGrid_VB_{i}_{hostId}", sc);
                        if (idB != ElementId.InvalidElementId) vb.Add(idB);
                    }
                }
            }
        }

        private void PerformDivision(List<ElementId> siblingIds, List<ElementId> hPlanes, List<ElementId> vaPlanes, List<ElementId> vbPlanes, ElementId sketchId, XYZ ySort)
        {
            if (hPlanes.Count > 0)
            {
                PartMaker pmH = _divisionService.Divide(siblingIds, hPlanes, sketchId);
                if (pmH != null) _divisionService.SetGroutGap(pmH, _config.HGroutGapFeet);
                _doc.Regenerate();
            }

            var stripIds = new List<ElementId>();
            foreach (var pid in siblingIds) stripIds.AddRange(_divisionService.GetAssociatedParts(pid));
            if (stripIds.Count == 0) return;

            if (_config.PatternType == TilePatternType.Grid)
            {
                if (vaPlanes.Count > 0)
                {
                    PartMaker pmV = _divisionService.Divide(stripIds, vaPlanes, sketchId);
                    if (pmV != null) _divisionService.SetGroutGap(pmV, _config.VGroutGapFeet);
                }
            }
            else
            {
                var sortedStrips = stripIds.Select(id => _doc.GetElement(id) as Part)
                    .Where(p => p != null && p.get_BoundingBox(null) != null)
                    .OrderBy(p => (p.get_BoundingBox(null).Min + p.get_BoundingBox(null).Max).DotProduct(ySort)).ToList();

                for (int i = 0; i < sortedStrips.Count; i++)
                {
                    var vps = (i % 2 == 0) ? vaPlanes : vbPlanes;
                    if (vps.Count > 0)
                    {
                        PartMaker pmV = _divisionService.Divide(new List<ElementId> { sortedStrips[i].Id }, vps, sketchId);
                        if (pmV != null) _divisionService.SetGroutGap(pmV, _config.VGroutGapFeet);
                    }
                }
            }
        }
    }
}
