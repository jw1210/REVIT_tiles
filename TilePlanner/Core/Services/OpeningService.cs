using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core.Services
{
    public class OpeningService
    {
        private readonly Document _doc;
        public OpeningService(Document doc) { _doc = doc; }

        public List<BoundingBoxXYZ> FindLinkedOpenings(Element host)
        {
            var res = new List<BoundingBoxXYZ>();
            var lks = new FilteredElementCollector(_doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>();
            BoundingBoxXYZ hb = host.get_BoundingBox(null);
            if (hb == null) return res;

            Outline ho = new Outline(hb.Min, hb.Max);
            foreach (var l in lks) {
                Document ld = l.GetLinkDocument();
                if (ld == null) continue;
                var ops = new FilteredElementCollector(ld).WhereElementIsNotElementType()
                    .WherePasses(new LogicalOrFilter(new ElementCategoryFilter(BuiltInCategory.OST_Windows), new ElementCategoryFilter(BuiltInCategory.OST_Doors)));

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

        // [V2.5 修正] 變更傳入參數為 hostOriginalId
        public void ExcludePartsInOpenings(ElementId hostOriginalId, List<BoundingBoxXYZ> opens)
        {
            var ids = PartUtils.GetAssociatedParts(_doc, hostOriginalId, false, true);
            foreach (var id in ids) {
                var p = _doc.GetElement(id) as Part;
                if (p == null || p.get_BoundingBox(null) == null) continue;
                XYZ c = (p.get_BoundingBox(null).Min + p.get_BoundingBox(null).Max) * 0.5;
                foreach (var ob in opens) {
                    if (c.X >= ob.Min.X && c.X <= ob.Max.X && c.Y >= ob.Min.Y && c.Y <= ob.Max.Y && c.Z >= ob.Min.Z && c.Z <= ob.Max.Z) {
                        p.get_Parameter(BuiltInParameter.DPART_EXCLUDED)?.Set(1);
                        break;
                    }
                }
            }
        }
    }
}
