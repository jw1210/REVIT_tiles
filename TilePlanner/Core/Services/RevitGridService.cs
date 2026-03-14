using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core.Services
{
    public class RevitGridService
    {
        private readonly Document _doc;
        public RevitGridService(Document doc) { _doc = doc; }

        public Category GetOrCreateSubcategory()
        {
            Category cat = Category.GetCategory(_doc, BuiltInCategory.OST_CLines);
            if (cat == null) return null;
            Category subCat = cat.SubCategories.Contains("磁磚切割網格") ? cat.SubCategories.get_Item("磁磚切割網格") : null;
            if (subCat == null)
            {
                subCat = _doc.Settings.Categories.NewSubcategory(cat, "磁磚切割網格");
                subCat.LineColor = new Color(0, 160, 0);
            }
            return subCat;
        }

        public void ClearOldGridElements(ElementId hostOriginalId)
        {
            // [V2.5 相容性修正] 確保 Revit 2024+ 64位元 ID 轉字串一致性
            string hostIdStr = hostOriginalId.ToString();

            var planes = new FilteredElementCollector(_doc)
                .OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
                .Where(rp => rp.Name != null && rp.Name.Contains("TileGrid_") && rp.Name.Contains(hostIdStr)).ToList();

            foreach (var p in planes) try { _doc.Delete(p.Id); } catch { }

            var dims = new FilteredElementCollector(_doc, _doc.ActiveView.Id)
                .OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => d.References != null).ToList();

            foreach (var dim in dims)
            {
                bool isGrid = false;
                try
                {
                    for (int i = 0; i < dim.References.Size; i++)
                    {
                        if (_doc.GetElement(dim.References.get_Item(i).ElementId) is ReferencePlane rp &&
                            rp.Name != null && rp.Name.Contains("TileGrid_") && rp.Name.Contains(hostIdStr))
                        {
                            isGrid = true; break;
                        }
                    }
                } catch { continue; }
                if (isGrid) try { _doc.Delete(dim.Id); } catch { }
            }
        }

        public ElementId CreateReferencePlane(XYZ p1, XYZ p2, XYZ normal, string name, Category subCat)
        {
            var rp = _doc.Create.NewReferencePlane(p1, p2, normal, _doc.ActiveView);
            if (rp == null) return ElementId.InvalidElementId;
            rp.Name = name;
            if (subCat != null)
            {
                Parameter sp = rp.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                if (sp != null && !sp.IsReadOnly) sp.Set(subCat.Id);
            }
            return rp.Id;
        }
    }
}
