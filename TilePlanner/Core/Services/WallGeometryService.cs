using Autodesk.Revit.DB;

namespace TilePlanner.Core.Services
{
    /// <summary>
    /// 負責處理牆面實體幾何操作 (如延伸牆面至轉角)
    /// </summary>
    public static class WallGeometryService
    {
        public static void ExtendWallToIncludeCorner(Wall wall, XYZ corner, double extensionDist)
        {
            LocationCurve lc = wall.Location as LocationCurve;
            if (lc == null) return;
            Curve curve = lc.Curve;
            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);

            int endIdx = (p0.DistanceTo(corner) < p1.DistanceTo(corner)) ? 0 : 1;
            
            // 1. 解開接合
            WallUtils.DisallowWallJoinAtEnd(wall, endIdx);
            
            // 2. 物理延伸 LocationLine
            XYZ endPt = curve.GetEndPoint(endIdx);
            XYZ otherPt = curve.GetEndPoint(1 - endIdx);
            XYZ dir = (endPt - otherPt).Normalize();
            
            XYZ newEnd = endPt + dir * extensionDist;
            if (endIdx == 0) lc.Curve = Line.CreateBound(newEnd, p1);
            else lc.Curve = Line.CreateBound(p0, newEnd);
        }
    }
}
