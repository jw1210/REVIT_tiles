using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace TilePlanner.Core.Services
{
    /// <summary>
    /// 提供純粹的幾何運算與網格分佈邏輯
    /// </summary>
    public static class GeometryService
    {
        public static double MmToFeet(double mm) => mm / 304.8;
        public static double FeetToMm(double feet) => feet * 304.8;

        /// <summary>
        /// 計算垂直與水平網格分佈點
        /// </summary>
        public static List<double> CalculateGridPoints(double min, double max, double interval, double gapHalf)
        {
            var points = new List<double>();
            // Flush Start 邏輯：起點從「邊界 - 半個灰縫」開始
            for (double p = min - gapHalf; p <= max + interval; p += interval)
            {
                points.Add(p);
            }
            return points;
        }

        /// <summary>
        /// 根據投影值與方向向量計算 3D 座標
        /// </summary>
        public static XYZ ProjectToXYZ(XYZ origin, XYZ xVec, XYZ yVec, double u, double v)
        {
            return origin + u * xVec + v * yVec;
        }
    }
}
