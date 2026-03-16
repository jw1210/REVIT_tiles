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
            string hostIdStr = hostOriginalId.ToString();

            // 1. [V3.5 新增] 清理舊的網格群組 (刪除 GroupType 會連帶刪除畫面上的 Group 實例)
            var groupTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(GroupType)).Cast<GroupType>()
                .Where(g => g.Name != null && g.Name.Contains("TileGrid_") && g.Name.Contains(hostIdStr)).ToList();
            foreach (var gt in groupTypes) try { _doc.Delete(gt.Id); } catch { }

            // 2. 清理參考平面實體
            var planes = new FilteredElementCollector(_doc)
                .OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
                .Where(rp => rp.Name != null && rp.Name.Contains("TileGrid_") && rp.Name.Contains(hostIdStr)).ToList();
            foreach (var p in planes) try { _doc.Delete(p.Id); } catch { }

            // 3. (保留向下相容) 清理舊版本遺留的標註鎖定
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
                            isGrid = true;
                            break;
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
