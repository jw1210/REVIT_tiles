using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using TilePlanner.Core.Services;

namespace TilePlanner.Core
{
    /// <summary>
    /// 磁磚計畫編排管理器 (Orchestrator)
    /// 負責協調各種服務以完成磁磚生成流程
    /// </summary>
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

            ICollection<ElementId> sourceElementIds = targetPart.GetSourceElementIds().Select(lr => lr.HostElementId).ToList();
            if (!sourceElementIds.Any()) return;
            Element hostOriginal = _doc.GetElement(sourceElementIds.First());

            // 1. 取得宿主最主要的面 (通常是面積最大的面)
            PlanarFace fullHostTargetFace = GetTargetFace(hostOriginal);
            if (fullHostTargetFace == null) return;

            // 2. 獲取所有同源的零件
            List<ElementId> siblingPartIds = GetSiblingParts(sourceElementIds);

            Plane plane = Plane.CreateByOriginAndBasis(fullHostTargetFace.Origin, fullHostTargetFace.XVector, fullHostTargetFace.YVector);

            using (Transaction t = new Transaction(_doc, "AntiGravity Tile V4.0 (Modular)"))
            {
                t.Start();

                // [模組：清理] 清空舊網格與隱形約束
                _gridService.ClearOldGridElements(hostElement.Id);
                _doc.Regenerate();

                SketchPlane sketchPlane = SketchPlane.Create(_doc, plane);
                List<BoundingBoxXYZ> openingBoxes = _openingService.FindLinkedOpenings(hostElement);

                List<ElementId> horizPlanes = new List<ElementId>();
                List<ElementId> vertPlanesSetA = new List<ElementId>();
                List<ElementId> vertPlanesSetB = new List<ElementId>();

                // [模組：網格生成] 建立貫穿式長刀
                CreateGridWeb(fullHostTargetFace, horizPlanes, vertPlanesSetA, vertPlanesSetB, hostElement.Id);
                _doc.Regenerate();

                // [模組：約束] 建立隱形約束鎖定
                GridConstraintManager constraintMgr = new GridConstraintManager(_doc, fullHostTargetFace, _config);
                constraintMgr.LockPlanes(horizPlanes, true);
                
                List<ElementId> allVertPlanes = new List<ElementId>(vertPlanesSetA);
                allVertPlanes.AddRange(vertPlanesSetB);
                constraintMgr.LockPlanes(allVertPlanes, false);
                
                _doc.Regenerate();

                // [模組：分割] 兩階段切割與獨立灰縫參數
                PerformDivision(siblingPartIds, horizPlanes, vertPlanesSetA, vertPlanesSetB, sketchPlane.Id, fullHostTargetFace.YVector);

                // [模組：視覺] 強制零件可見
                if (_doc.ActiveView != null) _doc.ActiveView.PartsVisibility = PartsVisibility.ShowPartsOnly;
                
                // [模組：開口] 排除開口處零件
                if (openingBoxes.Count > 0) _openingService.ExcludePartsInOpenings(targetPart.Id, openingBoxes);
                
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
                        if (f is PlanarFace pf && pf.Area > maxArea)
                        {
                            maxArea = pf.Area;
                            target = pf;
                        }
                    }
                }
            }
            return target;
        }

        private List<ElementId> GetSiblingParts(ICollection<ElementId> sourceIds)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Part))
                .Cast<Part>()
                .Where(p => p.GetSourceElementIds().Any(lr => sourceIds.Contains(lr.HostElementId)))
                .Select(p => p.Id)
                .ToList();
        }

        private void CreateGridWeb(PlanarFace face, List<ElementId> h, List<ElementId> va, List<ElementId> vb, ElementId hostId)
        {
            BoundingBoxUV bb = face.GetBoundingBox();
            XYZ x = face.XVector, y = face.YVector, n = face.FaceNormal, o = face.Origin;
            double ud = _config.CellWidthFeet, vd = _config.CellHeightFeet;
            double ext = 500.0 / 304.8; 
            
            Category sc = _gridService.GetOrCreateSubcategory();

            // 水平點位
            var hPoints = GeometryService.CalculateGridPoints(bb.Min.V, bb.Max.V, vd, _config.HGroutGapFeet / 2.0);
            for (int i = 0; i < hPoints.Count; i++)
            {
                XYZ p1 = GeometryService.ProjectToXYZ(o, x, y, bb.Min.U - ext, hPoints[i]);
                XYZ p2 = GeometryService.ProjectToXYZ(o, x, y, bb.Max.U + ext, hPoints[i]);
                var id = _gridService.CreateReferencePlane(p1, p2, n, $"TileGrid_H_{i}_{hostId}", sc);
                if (id != ElementId.InvalidElementId) h.Add(id);
            }

            // 垂直點位
            var vPoints = GeometryService.CalculateGridPoints(bb.Min.U, bb.Max.U, ud, _config.VGroutGapFeet / 2.0);
            for (int i = 0; i < vPoints.Count; i++)
            {
                XYZ p1_A = GeometryService.ProjectToXYZ(o, x, y, vPoints[i], bb.Min.V - ext);
                XYZ p2_A = GeometryService.ProjectToXYZ(o, x, y, vPoints[i], bb.Max.V + ext);
                var idA = _gridService.CreateReferencePlane(p1_A, p2_A, n, $"TileGrid_VA_{i}_{hostId}", sc);
                if (idA != ElementId.InvalidElementId) va.Add(idA);

                if (_config.PatternType == TilePatternType.RunningBond)
                {
                    double offsetU = vPoints[i] + ud * _config.RunningBondOffset;
                    XYZ p1_B = GeometryService.ProjectToXYZ(o, x, y, offsetU, bb.Min.V - ext);
                    XYZ p2_B = GeometryService.ProjectToXYZ(o, x, y, offsetU, bb.Max.V + ext);
                    var idB = _gridService.CreateReferencePlane(p1_B, p2_B, n, $"TileGrid_VB_{i}_{hostId}", sc);
                    if (idB != ElementId.InvalidElementId) vb.Add(idB);
                }
            }
        }

        private void PerformDivision(List<ElementId> siblingIds, List<ElementId> hPlanes, List<ElementId> vaPlanes, List<ElementId> vbPlanes, ElementId sketchId, XYZ ySort)
        {
            // 階段 A：水平切割
            PartMaker pmH = _divisionService.Divide(siblingIds, hPlanes, sketchId);
            if (pmH != null) _divisionService.SetGroutGap(pmH, _config.HGroutGapFeet);
            _doc.Regenerate();

            // 抓取子零件
            var stripIds = new List<ElementId>();
            foreach (var pid in siblingIds) stripIds.AddRange(_divisionService.GetAssociatedParts(pid));

            if (_config.PatternType == TilePatternType.Grid)
            {
                // 階段 B：垂直切割 (正排)
                if (stripIds.Count > 0)
                {
                    PartMaker pmV = _divisionService.Divide(stripIds, vaPlanes, sketchId);
                    if (pmV != null) _divisionService.SetGroutGap(pmV, _config.VGroutGapFeet);
                }
            }
            else
            {
                // 階段 B：垂直切割 (交丁)
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
