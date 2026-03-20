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
        /// 計算分佈點 (輔助對位)。此方法會從 start 向兩側延伸以覆蓋 [min, max] 區間
        /// </summary>
        public static List<double> CalculateGridPoints(double min, double max, double start, double interval)
        {
            var points = new List<double>();
            
            // 向下找起點 (確保覆蓋 min)
            double p = start;
            while (p > min - interval) p -= interval;

            // 向上填充 (直到超過 max)
            for (; p <= max + interval; p += interval)
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
