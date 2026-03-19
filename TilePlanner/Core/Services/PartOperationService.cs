using System.Collections.Generic;
using Autodesk.Revit.DB;
using TilePlanner.Core.Utils;

namespace TilePlanner.Core.Services
{
    /// <summary>
    /// 負責處理 Part 零件的修剪、分割與廢料隱藏
    /// 保留 V4.1.21 最穩定的 Signed Distance 廢料判定邏輯
    /// </summary>
    public static class PartOperationService
    {
        public static bool CutAndExcludeWasteZeroGap(Document doc, ElementId partId, XYZ origin, XYZ miterNormal, double offset)
        {
            Part p = doc.GetElement(partId) as Part;
            if (p == null) return false;

            XYZ centroid = p.GetCentroid();
            XYZ vecToCentroid = (centroid - origin).Normalize();
            
            XYZ inwardDir = (miterNormal.DotProduct(vecToCentroid) > 0) ? miterNormal : -miterNormal;
            XYZ cutPos = origin + inwardDir * offset; 

            Plane plane = Plane.CreateByNormalAndOrigin(inwardDir, cutPos);
            SketchPlane sp = SketchPlane.Create(doc, plane);
            // 恆定使用垂直刀刃確保切穿所有幾何
            XYZ axis = XYZ.BasisZ;
            Line cutLine = Line.CreateBound(cutPos - axis * 2500.0, cutPos + axis * 2500.0);

            try
            {
                PartUtils.DivideParts(doc, new List<ElementId> { partId }, new List<ElementId>(), new List<Curve> { cutLine }, sp.Id);
                doc.Regenerate(); // 必須在 Transaction 內

                ICollection<ElementId> results = PartUtils.GetAssociatedParts(doc, partId, false, true);
                foreach (ElementId rid in results)
                {
                    Part sub = doc.GetElement(rid) as Part;
                    if (sub == null) continue;
                    XYZ vecSub = (sub.GetCentroid() - cutPos);
                    // 根據內向向量作 Signed Distance 檢查
                    if (vecSub.DotProduct(inwardDir) < -1e-6)
                    {
                        sub.get_Parameter(BuiltInParameter.DPART_EXCLUDED)?.Set(1);
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }
}
