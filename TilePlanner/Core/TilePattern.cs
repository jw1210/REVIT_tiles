namespace TilePlanner.Core
{
    /// <summary>
    /// 磁磚排列模式
    /// </summary>
    public enum TilePatternType
    {
        /// <summary>正排 — 磁磚對齊排列</summary>
        Grid,

        /// <summary>交丁排 — 每隔一行偏移指定百分比</summary>
        RunningBond
    }

    /// <summary>
    /// 排列模式相關計算
    /// </summary>
    public static class TilePatternHelper
    {
        /// <summary>
        /// 取得指定行的水平偏移量 (feet)
        /// </summary>
        /// <param name="config">磁磚配置</param>
        /// <param name="rowIndex">行索引 (0-based)</param>
        /// <returns>該行的水平偏移量 (feet)</returns>
        public static double GetRowOffset(TileConfig config, int rowIndex)
        {
            if (config.PatternType == TilePatternType.Grid)
            {
                // 正排：所有行都不偏移
                return 0.0;
            }

            // 交丁排：奇數行偏移
            if (rowIndex % 2 == 1)
            {
                // 偏移量 = 磁磚寬度(含灰縫) × 偏移百分比
                return config.CellWidthFeet * config.RunningBondOffset;
            }

            return 0.0;
        }

        /// <summary>
        /// 取得排列模式的顯示名稱
        /// </summary>
        public static string GetPatternDisplayName(TilePatternType patternType)
        {
            switch (patternType)
            {
                case TilePatternType.Grid:
                    return "正排";
                case TilePatternType.RunningBond:
                    return "交丁排";
                default:
                    return patternType.ToString();
            }
        }
    }
}
