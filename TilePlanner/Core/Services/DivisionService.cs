using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core.Services
{
    /// <summary>
    /// 處理零件分割與灰縫參數寫入
    /// </summary>
    public class DivisionService
    {
        private readonly Document _doc;

        public DivisionService(Document doc)
        {
            _doc = doc;
        }

        public PartMaker Divide(ICollection<ElementId> partIds, ICollection<ElementId> planeIds, ElementId sketchPlaneId)
        {
            return PartUtils.DivideParts(_doc, partIds, planeIds, new List<Curve>(), sketchPlaneId);
        }

        public void SetGroutGap(PartMaker maker, double gapFeet)
        {
            if (maker == null || gapFeet <= 0) return;

            Parameter p = maker.get_Parameter((BuiltInParameter)(-1140510)) ?? // PART_MAKER_DIVIDER_GAP
                          maker.LookupParameter("Divider gap") ??
                          maker.LookupParameter("分割間隙") ??
                          maker.LookupParameter("分隔間隙") ??
                          maker.LookupParameter("灰縫") ??
                          maker.LookupParameter("Grout");

            if (p == null)
            {
                foreach (Parameter param in maker.Parameters)
                {
                    if (param.Definition.Name.Contains("間隙") || param.Definition.Name.Contains("Gap") || param.Definition.Name.Contains("縫"))
                    {
                        p = param;
                        break;
                    }
                }
            }

            if (p != null && !p.IsReadOnly) p.Set(gapFeet);
        }

        public List<ElementId> GetAssociatedParts(ElementId partId)
        {
            return PartUtils.GetAssociatedParts(_doc, partId, false, true).ToList();
        }
    }
}
