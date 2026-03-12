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
            if (!(hostElement is Part targetPart)) return;

            ICollection<ElementId> sourceElementIds = targetPart.GetSourceElementIds().Select(lr => lr.HostElementId).ToList();
            if (!sourceElementIds.Any()) return;
            Element hostOriginal = _doc.GetElement(sourceElementIds.First());

            Options optHost = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement hostGeom = hostOriginal.get_Geometry(optHost);
            PlanarFace fullHostTargetFace = null;
            double maxAreaFromHost = 0;

            foreach (GeometryObject obj in hostGeom)
            {
                if (obj is Solid s && s.Faces.Size > 0)
                {
                    foreach (Face f in s.Faces)
                    {
                        if (f is PlanarFace pf && pf.Area > maxAreaFromHost)
                        {
                            maxAreaFromHost = pf.Area;
                            fullHostTargetFace = pf;
                        }
                    }
                }
            }

            if (fullHostTargetFace == null) return;

            List<ElementId> siblingPartIds = new FilteredElementCollector(_doc)
                .OfClass(typeof(Part))
                .Cast<Part>()
                .Where(p => p.GetSourceElementIds().Any(lr => sourceElementIds.Contains(lr.HostElementId)))
                .Select(p => p.Id)
                .ToList();

            Plane plane = Plane.CreateByOriginAndBasis(fullHostTargetFace.Origin, fullHostTargetFace.XVector, fullHostTargetFace.YVector);

            using (Transaction t = new Transaction(_doc, "AntiGravity Tile V2.4.0"))
            {
                t.Start();

                // [V2.4] 模組三：舊網格清除機制 — 一鍵重繪時清除舊標註與參照平面
                ClearOldGridElements(hostElement.Id);
                _doc.Regenerate();

                SketchPlane sketchPlane = SketchPlane.Create(_doc, plane);
                List<BoundingBoxXYZ> openingBoxes = FindLinkedOpenings(hostElement);

                List<ElementId> horizPlanes = new List<ElementId>();
                List<ElementId> vertPlanesSetA = new List<ElementId>();
                List<ElementId> vertPlanesSetB = new List<ElementId>();

                // [V2.4] 模組一：貫穿式長刀與邊界延伸
                CreateSingleBladeGrid(fullHostTargetFace, horizPlanes, vertPlanesSetA, vertPlanesSetB, hostElement.Id, openingBoxes);
                _doc.Regenerate();

                // [V2.4] 模組二：連續標註鎖定與整體平移
                GridConstraintManager constraintMgr = new GridConstraintManager(_doc, fullHostTargetFace, _config);
                constraintMgr.LockPlanes(horizPlanes, true);
                
                List<ElementId> allVertPlanes = new List<ElementId>(vertPlanesSetA);
                allVertPlanes.AddRange(vertPlanesSetB);
                constraintMgr.LockPlanes(allVertPlanes, false);
                
                _doc.Regenerate();

                // 執行實體零件分割
                if (_config.PatternType == TilePatternType.Grid) {
                    List<ElementId> all = new List<ElementId>(horizPlanes);
                    all.AddRange(vertPlanesSetA);
                    PartUtils.DivideParts(_doc, siblingPartIds, all, new List<Curve>(), sketchPlane.Id);
                    foreach (var pid in siblingPartIds) SetPartMakerDividerGap(pid, _config.HGroutGapFeet);
                } else {
                    PartUtils.DivideParts(_doc, siblingPartIds, horizPlanes, new List<Curve>(), sketchPlane.Id);
                    _doc.Regenerate();
                    foreach (var pid in siblingPartIds) SetPartMakerDividerGap(pid, _config.HGroutGapFeet);

                    var stripIds = new List<ElementId>();
                    foreach (var pid in siblingPartIds) {
                        var associated = PartUtils.GetAssociatedParts(_doc, pid, false, true);
                        if (associated != null) stripIds.AddRange(associated);
                    }

                    XYZ yDirSort = fullHostTargetFace.YVector;
                    var sortedStrips = stripIds.Select(id => _doc.GetElement(id) as Part)
                        .Where(p => p != null && p.get_BoundingBox(null) != null)
                        .OrderBy(p => (p.get_BoundingBox(null).Min + p.get_BoundingBox(null).Max).DotProduct(yDirSort)).ToList();

                    for (int i = 0; i < sortedStrips.Count; i++) {
                        var vps = (i % 2 == 0) ? vertPlanesSetA : vertPlanesSetB;
                        if (vps.Count > 0) PartUtils.DivideParts(_doc, new List<ElementId> { sortedStrips[i].Id }, vps, new List<Curve>(), sketchPlane.Id);
                    }
                    _doc.Regenerate();
                    foreach (var s in sortedStrips) SetPartMakerDividerGap(s.Id, _config.VGroutGapFeet);
                }
                
                // [V2.4] 模組四：雙向灰縫自動恢復
                if (openingBoxes.Count > 0) ExcludeOpeningParts(targetPart.Id, openingBoxes);
                
                t.Commit();
            }
        }

        /// <summary>
        /// [V2.4 模組三] 清除該宿主相關的舊網格元素 
        /// 包括舊參照平面與舊標註，防止約束衝突
        /// </summary>
        private void ClearOldGridElements(ElementId hostElementId)
        {
            // 1. 尋找並刪除舊參照平面（按名稱包含該宿主 ID）
            var oldReferencePlanes = new FilteredElementCollector(_doc)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .Where(rp => rp.Name.Contains($"TileGrid_") && rp.Name.Contains(hostElementId.ToString()))
                .ToList();

            foreach (var plane in oldReferencePlanes)
            {
                try
                {
                    _doc.Delete(plane.Id);
                }
                catch
                {
                    // 若刪除失敗略過（可能已有依賴關係）
                }
            }

            // 2. 尋找並刪除舊標註（包含該宿主相關參照平面的連續標註）
            var allDimensions = new FilteredElementCollector(_doc)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .Where(d => d != null && d.References != null)
                .ToList();

            var oldDimensionIds = new List<ElementId>();
            foreach (var dim in allDimensions)
            {
                bool isGridDimension = false;
                try
                {
                    for (int i = 0; i < dim.References.Size; i++)
                    {
                        Reference rf = dim.References.get_Item(i);
                        Element refElem = _doc.GetElement(rf.ElementId);
                        if (refElem is ReferencePlane rp)
                        {
                            if (rp.Name.Contains($"TileGrid_") && rp.Name.Contains(hostElementId.ToString()))
                            {
                                isGridDimension = true;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }

                if (isGridDimension)
                {
                    oldDimensionIds.Add(dim.Id);
                }
            }

            foreach (var id in oldDimensionIds)
            {
                try
                {
                    _doc.Delete(id);
                }
                catch
                {
                    // 若刪除失敗略過
                }
            }
        }

        private void CreateSingleBladeGrid(PlanarFace face, List<ElementId> h, List<ElementId> va, List<ElementId> vb, ElementId hostId, List<BoundingBoxXYZ> obs)
        {
            BoundingBoxUV bb = face.GetBoundingBox();
            XYZ o = face.Origin, x = face.XVector, y = face.YVector, n = face.FaceNormal;
            double ud = _config.CellWidthFeet, vd = _config.CellHeightFeet;
            // 參考線向外延伸各 15cm (總長 +30cm)
            double ext = 150.0 / 304.8;
            Category sc = GetOrCreateSubcategory();
            Func<double, double, XYZ> tw = (u, v) => o + u * x + v * y;

            Func<XYZ, XYZ, string, ElementId> cr = (p1, p2, name) => {
                var rp = _doc.Create.NewReferencePlane(p1, p2, n, _doc.ActiveView);
                if (rp == null) return ElementId.InvalidElementId;
                rp.Name = name;
                if (sc != null) {
                    Parameter sp = rp.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                    if (sp != null && !sp.IsReadOnly) sp.Set(sc.Id);
                    else foreach (Parameter p in rp.Parameters) if (p.Definition.Name.Contains("Subcategory") || p.Definition.Name.Contains("子品類")) { p.Set(sc.Id); break; }
                }
                return rp.Id;
            };

            // 僅在宿主面範圍內建立參考線，並在兩端各延伸 15cm
            int mh = (int)Math.Floor(bb.Min.V / vd) - 1;
            int xh = (int)Math.Ceiling(bb.Max.V / vd) + 1;
            for (int i = mh; i <= xh; i++) {
                double vp = i * vd;
                var id = cr(
                    tw(bb.Min.U - ext, vp),
                    tw(bb.Max.U + ext, vp),
                    $"TileGrid_H_{i}_{hostId}");
                if (id != ElementId.InvalidElementId) h.Add(id);
            }

            int mv = (int)Math.Floor(bb.Min.U / ud) - 1;
            int xv = (int)Math.Ceiling(bb.Max.U / ud) + 1;
            for (int i = mv; i <= xv; i++) {
                double up = i * ud;
                var id = cr(
                    tw(up, bb.Min.V - ext),
                    tw(up, bb.Max.V + ext),
                    $"TileGrid_VA_{i}_{hostId}");
                if (id != ElementId.InvalidElementId) va.Add(id);
                if (_config.PatternType == TilePatternType.RunningBond) {
                    var idb = cr(
                        tw(up + ud * _config.RunningBondOffset, bb.Min.V - ext),
                        tw(up + ud * _config.RunningBondOffset, bb.Max.V + ext),
                        $"TileGrid_VB_{i}_{hostId}");
                    if (idb != ElementId.InvalidElementId) vb.Add(idb);
                }
            }
        }

        private void SetPartMakerDividerGap(ElementId sid, double g)
        {
            if (g <= 0) return;
            var pms = new FilteredElementCollector(_doc).OfClass(typeof(PartMaker)).Cast<PartMaker>();
            foreach (var pm in pms)
            {
                if (!pm.GetSourceElementIds().Any(lr => lr.HostElementId == sid)) continue;

                // 嘗試以多種可能名稱取得「分割間隙 / 灰縫」參數
                Parameter p =
                    pm.LookupParameter("Divider gap") ??          // 英文介面
                    pm.LookupParameter("分割間隙") ??              // 常見繁中翻譯 1
                    pm.LookupParameter("分隔間隙") ??              // 常見繁中翻譯 2（保險）
                    pm.LookupParameter("灰縫");                    // 自訂/中文命名

                if (p != null && !p.IsReadOnly)
                {
                    p.Set(g);
                }
            }
        }

        private List<BoundingBoxXYZ> FindLinkedOpenings(Element host)
        {
            var res = new List<BoundingBoxXYZ>();
            var lks = new FilteredElementCollector(_doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>();
            BoundingBoxXYZ hb = host.get_BoundingBox(null);
            if (hb == null) return res;
            Outline ho = new Outline(hb.Min, hb.Max);
            foreach (var l in lks) {
                Document ld = l.GetLinkDocument();
                if (ld == null) continue;
                var ops = new FilteredElementCollector(ld).WhereElementIsNotElementType().WherePasses(new LogicalOrFilter(new ElementCategoryFilter(BuiltInCategory.OST_Windows), new ElementCategoryFilter(BuiltInCategory.OST_Doors)));
                Transform t = l.GetTransform();
                foreach (var op in ops) {
                    BoundingBoxXYZ ob = op.get_BoundingBox(null);
                    if (ob == null) continue;
                    XYZ mi = t.OfPoint(ob.Min), ma = t.OfPoint(ob.Max);
                    if (ho.Intersects(new Outline(mi, ma), 0.5)) res.Add(new BoundingBoxXYZ { Min = mi, Max = ma });
                }
            }
            return res;
        }

        private void ExcludeOpeningParts(ElementId rid, List<BoundingBoxXYZ> obs)
        {
            var ids = PartUtils.GetAssociatedParts(_doc, rid, false, true);
            foreach (var id in ids) {
                var p = _doc.GetElement(id) as Part;
                if (p == null || p.get_BoundingBox(null) == null) continue;
                XYZ c = (p.get_BoundingBox(null).Min + p.get_BoundingBox(null).Max) * 0.5;
                foreach (var ob in obs) if (c.X >= ob.Min.X && c.X <= ob.Max.X && c.Y >= ob.Min.Y && c.Y <= ob.Max.Y && c.Z >= ob.Min.Z && c.Z <= ob.Max.Z) { p.get_Parameter(BuiltInParameter.DPART_EXCLUDED)?.Set(1); break; }
            }
        }

        private Category GetOrCreateSubcategory()
        {
            Category cat = Category.GetCategory(_doc, BuiltInCategory.OST_CLines);
            if (cat == null) return null;

            // 優先使用新版名稱「磁磚切割網格」，若不存在則回退舊名稱「磁磚計畫刀網」
            Category subCat = null;
            if (cat.SubCategories.Contains("磁磚切割網格"))
            {
                subCat = cat.SubCategories.get_Item("磁磚切割網格");
            }
            else if (cat.SubCategories.Contains("磁磚計畫刀網"))
            {
                subCat = cat.SubCategories.get_Item("磁磚計畫刀網");
            }
            else
            {
                try
                {
                    subCat = _doc.Settings.Categories.NewSubcategory(cat, "磁磚切割網格");
                    subCat.LineColor = new Color(0, 160, 0);
                }
                catch
                {
                    return null;
                }
            }

            // 統一使用虛線線型 (若有可用樣式)
            try
            {
                LinePatternElement dashPattern = null;
                var collector = new FilteredElementCollector(_doc).OfClass(typeof(LinePatternElement));
                foreach (LinePatternElement lp in collector)
                {
                    string n = lp.Name ?? string.Empty;
                    if (n.Contains("虛線") || n.Contains("Dash") || n.Contains("Dashed"))
                    {
                        dashPattern = lp;
                        break;
                    }
                }

                if (dashPattern != null)
                {
                    // Revit 2024/2025 皆支援帶 GraphicsStyleType 參數的多載
                    subCat.SetLinePatternId(dashPattern.Id, GraphicsStyleType.Projection);
                }
            }
            catch { }

            return subCat;
        }
    }
}
