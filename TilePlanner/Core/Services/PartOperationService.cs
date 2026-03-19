using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>
        /// 開啟或關閉 Part 的造型控點 (Shape Handles)
        /// </summary>
        public static void SetPartShapeModified(Part p, bool modified)
        {
            Parameter param = p.get_Parameter(BuiltInParameter.DPART_SHAPE_MODIFIED);
            if (param != null && !param.IsReadOnly)
            {
                param.Set(modified ? 1 : 0);
            }
        }

        public static bool PerformTrueDiagonalCut(Document doc, List<Part> parts, XYZ ptInner, XYZ vDiag, Wall hostWall)
        {
            if (parts.Count == 0) return false;

            // 切割面法向量 (垂直於對角向量與 Z 軸)
            XYZ nTdp = vDiag.CrossProduct(XYZ.BasisZ).Normalize();

            Curve curve = (hostWall.Location as LocationCurve)?.Curve;
            if (curve == null) return false;

            XYZ end0 = curve.GetEndPoint(0);
            XYZ end1 = curve.GetEndPoint(1);
            XYZ farPt = (end0.DistanceTo(ptInner) > end1.DistanceTo(ptInner)) ? end0 : end1;

            // 領土特徵：判定哪一側是該保留的「本體」
            double territorySign = nTdp.DotProduct(farPt - ptInner);

            // 建立水平切割平面 (以 ptInner 為原點)
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, ptInner);
            SketchPlane sp = SketchPlane.Create(doc, plane);

            // 建立長 10 呎的切割線
            Line cutLine = Line.CreateBound(ptInner - vDiag * 5.0, ptInner + vDiag * 5.0);

            bool success = true;
            foreach (Part p in parts)
            {
                try
                {
                    PartUtils.DivideParts(doc, new List<ElementId> { p.Id }, new List<ElementId>(), new List<Curve> { cutLine }, sp.Id);
                    doc.Regenerate();

                    ICollection<ElementId> results = PartUtils.GetAssociatedParts(doc, p.Id, false, true);
                    foreach (ElementId rid in results)
                    {
                        Part sub = doc.GetElement(rid) as Part;
                        if (sub == null) continue;

                        XYZ subCentroid = sub.GetCentroid();
                        double subSign = nTdp.DotProduct(subCentroid - ptInner);

                        // 簽名判斷：若位於非領土側則排除 (廢料)
                        if (territorySign > 0 && subSign < -1e-4) sub.get_Parameter(BuiltInParameter.DPART_EXCLUDED)?.Set(1);
                        else if (territorySign < 0 && subSign > 1e-4) sub.get_Parameter(BuiltInParameter.DPART_EXCLUDED)?.Set(1);
                    }
                }
                catch { success = false; }
            }
            return success;
        }

        public static XYZ GetIntersection2D(XYZ p1, XYZ n1, XYZ p2, XYZ n2)
        {
            double c1 = n1.X * p1.X + n1.Y * p1.Y;
            double c2 = n2.X * p2.X + n2.Y * p2.Y;
            double det = n1.X * n2.Y - n1.Y * n2.X;
            if (Math.Abs(det) < 1e-6) return null;
            double x = (c1 * n2.Y - c2 * n1.Y) / det;
            double y = (n1.X * c2 - n2.X * c1) / det;
            return new XYZ(x, y, 0);
        }

        public static PlanarFace GetOuterFace(Part p, Wall wall)
        {
            XYZ wallOrient = wall.Orientation; // 指向牆體外部的向量
            return p.GetSolid()?.Faces.Cast<PlanarFace>()
                .FirstOrDefault(f => Math.Abs(f.FaceNormal.Z) < 1e-3 && f.FaceNormal.DotProduct(wallOrient) > 0.9);
        }

        public static PlanarFace GetInnerFace(Part p, Wall wall)
        {
            XYZ wallOrient = wall.Orientation;
            return p.GetSolid()?.Faces.Cast<PlanarFace>()
                .FirstOrDefault(f => Math.Abs(f.FaceNormal.Z) < 1e-3 && f.FaceNormal.DotProduct(wallOrient) < -0.9);
        }

        /// <summary>
        /// 獲取零件的「端面」(面向轉角的那個面)，必須是最靠近轉角交點的那個
        /// </summary>
        public static PlanarFace GetEndFace(Part p, XYZ wallDir, XYZ cornerPt)
        {
            // 端面的法向量應該與牆體方向 (wallDir) 平行
            var endFaces = p.GetSolid()?.Faces.Cast<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 1e-3 && Math.Abs(f.FaceNormal.DotProduct(wallDir)) > 0.9)
                .ToList();

            if (endFaces == null || endFaces.Count == 0) return null;

            // 選擇離轉角交點最近的一個面
            return endFaces.OrderBy(f => f.Origin.DistanceTo(cornerPt)).FirstOrDefault();
        }

        private static PlanarFace GetSideFaceByDistance(Part p, Wall wall, bool wantFarther)
        {
            Curve wallCurve = (wall.Location as LocationCurve)?.Curve;
            if (!(wallCurve is Line wl)) return null;

            XYZ wallDir = wl.Direction.Normalize();
            var sideFaces = p.GetSolid()?.Faces.Cast<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 1e-3 && Math.Abs(f.FaceNormal.DotProduct(wallDir)) < 1e-3)
                .ToList();

            if (sideFaces == null || sideFaces.Count < 2) return null;

            double d0 = DistanceToInfiniteLine(sideFaces[0].Origin, wl);
            double d1 = DistanceToInfiniteLine(sideFaces[1].Origin, wl);

            if (wantFarther) return d0 > d1 ? sideFaces[0] : sideFaces[1];
            return d0 < d1 ? sideFaces[0] : sideFaces[1];
        }

        private static double DistanceToInfiniteLine(XYZ p, Line line)
        {
            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            XYZ dir = (p1 - p0).Normalize();
            XYZ v = p - p0;
            return v.CrossProduct(dir).GetLength();
        }

        // 保留原有的簡化方法以供相容，但主要使用 PerformTrueDiagonalCut
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
            XYZ axis = XYZ.BasisZ;
            Line cutLine = Line.CreateBound(cutPos - axis * 2500.0, cutPos + axis * 2500.0);
            try
            {
                PartUtils.DivideParts(doc, new List<ElementId> { partId }, new List<ElementId>(), new List<Curve> { cutLine }, sp.Id);
                doc.Regenerate();
                ICollection<ElementId> results = PartUtils.GetAssociatedParts(doc, partId, false, true);
                foreach (ElementId rid in results)
                {
                    Part sub = doc.GetElement(rid) as Part;
                    if (sub == null) continue;
                    XYZ vecSub = (sub.GetCentroid() - cutPos);
                    if (vecSub.DotProduct(inwardDir) < -1e-6) sub.get_Parameter(BuiltInParameter.DPART_EXCLUDED)?.Set(1);
                }
                return true;
            }
            catch { return false; }
        }
    }
}
