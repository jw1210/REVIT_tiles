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
                SketchPlane sketchPlane = SketchPlane.Create(_doc, plane);
                List<BoundingBoxXYZ> openingBoxes = FindLinkedOpenings(hostElement);

                List<ElementId> horizPlanes = new List<ElementId>();
                List<ElementId> vertPlanesSetA = new List<ElementId>();
                List<ElementId> vertPlanesSetB = new List<ElementId>();

                CreateSingleBladeGrid(fullHostTargetFace, horizPlanes, vertPlanesSetA, vertPlanesSetB, hostElement.Id, openingBoxes);
                _doc.Regenerate();

                Action<List<ElementId>, bool> lockPlanes = (planeIds, isHoriz) =>
                {
                    if (planeIds.Count < 2) return;
                    XYZ xDir = fullHostTargetFace.XVector;
                    XYZ yDir = fullHostTargetFace.YVector;
                    var sortedIds = planeIds.OrderBy(id => {
                        var rp = _doc.GetElement(id) as ReferencePlane;
                        return isHoriz ? rp.BubbleEnd.DotProduct(yDir) : rp.BubbleEnd.DotProduct(xDir);
                    }).ToList();

                    ReferenceArray refArray = new ReferenceArray();
                    foreach (var id in sortedIds) {
                        var rp = _doc.GetElement(id) as ReferencePlane;
                        refArray.Append(rp.GetReference());
                    }
                    
                    if (refArray.Size >= 2) {
                        try {
                            double offset = 5000.0 / 304.8;
                            BoundingBoxUV bbox = fullHostTargetFace.GetBoundingBox();
                            XYZ p1 = fullHostTargetFace.Origin + (bbox.Min.U - offset) * xDir + (bbox.Min.V - offset) * yDir;
                            XYZ p2 = fullHostTargetFace.Origin + (bbox.Max.U + offset) * xDir + (bbox.Max.V + offset) * yDir;
                            Dimension dim = _doc.Create.NewDimension(_doc.ActiveView, Line.CreateBound(p1, p2), refArray);
                            if (dim != null) {
                                if (dim.Segments.Size > 0) foreach (DimensionSegment s in dim.Segments) s.IsLocked = true;
                                else dim.IsLocked = true;
                                _doc.ActiveView.HideElements(new List<ElementId> { dim.Id });
                            }
                        } catch {}
                    }
                };

                lockPlanes(horizPlanes, true);
                List<ElementId> allV = new List<ElementId>(vertPlanesSetA);
                allV.AddRange(vertPlanesSetB);
                lockPlanes(allV, false);
                _doc.Regenerate();

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
                if (openingBoxes.Count > 0) ExcludeOpeningParts(targetPart.Id, openingBoxes);
                t.Commit();
            }
        }

        private void CreateSingleBladeGrid(PlanarFace face, List<ElementId> h, List<ElementId> va, List<ElementId> vb, ElementId hostId, List<BoundingBoxXYZ> obs)
        {
            BoundingBoxUV bb = face.GetBoundingBox();
            XYZ o = face.Origin, x = face.XVector, y = face.YVector, n = face.FaceNormal;
            double ud = _config.CellWidthFeet, vd = _config.CellHeightFeet;
            double sr = 50000.0 / 304.8, se = 1500.0 / 304.8;
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

            int mh = -(int)Math.Ceiling(sr / vd), xh = (int)Math.Ceiling((bb.Max.V - bb.Min.V + sr) / vd);
            for (int i = mh; i <= xh; i++) {
                double vp = bb.Min.V + i * vd;
                var id = cr(tw(bb.Min.U - se, vp), tw(bb.Max.U + se, vp), $"TileGrid_H_{i}_{hostId}");
                if (id != ElementId.InvalidElementId) h.Add(id);
            }

            int mv = -(int)Math.Ceiling(sr / ud), xv = (int)Math.Ceiling((bb.Max.U - bb.Min.U + sr) / ud);
            for (int i = mv; i <= xv; i++) {
                double up = bb.Min.U + i * ud;
                var id = cr(tw(up, bb.Min.V - se), tw(up, bb.Max.V + se), $"TileGrid_VA_{i}_{hostId}");
                if (id != ElementId.InvalidElementId) va.Add(id);
                if (_config.PatternType == TilePatternType.RunningBond) {
                    var idb = cr(tw(up + ud * _config.RunningBondOffset, bb.Min.V - se), tw(up + ud * _config.RunningBondOffset, bb.Max.V + se), $"TileGrid_VB_{i}_{hostId}");
                    if (idb != ElementId.InvalidElementId) vb.Add(idb);
                }
            }
        }

        private void SetPartMakerDividerGap(ElementId sid, double g)
        {
            if (g <= 0) return;
            var pms = new FilteredElementCollector(_doc).OfClass(typeof(PartMaker)).Cast<PartMaker>();
            foreach (var pm in pms) if (pm.GetSourceElementIds().Any(lr => lr.HostElementId == sid)) {
                Parameter p = pm.LookupParameter("Divider gap") ?? pm.LookupParameter("分割間隙") ?? pm.LookupParameter("灰縫");
                if (p != null && !p.IsReadOnly) p.Set(g);
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
            if (cat.SubCategories.Contains("磁磚計畫刀網")) return cat.SubCategories.get_Item("磁磚計畫刀網");
            try { var sub = _doc.Settings.Categories.NewSubcategory(cat, "磁磚計畫刀網"); sub.LineColor = new Color(0, 160, 0); return sub; } catch { return null; }
        }
    }
}
