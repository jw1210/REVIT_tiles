namespace TilePlanner.Core
{
    /// <summary>
    /// 磁磚與灰縫的參數配置
    /// </summary>
    public class TileConfig
    {
        // ===== 磁磚設定 =====

        /// <summary>磁磚寬度 (mm)</summary>
        public double TileWidth { get; set; } = 200;

        /// <summary>磁磚高度 (mm)</summary>
        public double TileHeight { get; set; } = 200;

        /// <summary>磁磚厚度 (mm)</summary>
        public double TileThickness { get; set; } = 10;

        // ===== 灰縫設定（以竪框建置）=====

        /// <summary>灰縫寬度 (mm)</summary>
        public double GroutWidth { get; set; } = 3;

        /// <summary>灰縫厚度/深度 (mm)</summary>
        public double GroutThickness { get; set; } = 3;

        // ===== 排列模式 =====

        /// <summary>排列模式</summary>
        public TilePatternType PatternType { get; set; } = TilePatternType.Grid;

        /// <summary>
        /// 交丁偏移百分比 (0.0 ~ 1.0)
        /// 例如：0.5 = 50% (半磚交丁), 0.37 = 37分, 0.55 = 55分
        /// 僅在 RunningBond 模式下有效
        /// </summary>
        public double RunningBondOffset { get; set; } = 0.5;

        // ===== 轉換輔助方法 =====

        /// <summary>磁磚寬度 (feet，Revit 內部單位)</summary>
        public double TileWidthFeet => MmToFeet(TileWidth);

        /// <summary>磁磚高度 (feet)</summary>
        public double TileHeightFeet => MmToFeet(TileHeight);

        /// <summary>磁磚厚度 (feet)</summary>
        public double TileThicknessFeet => MmToFeet(TileThickness);

        /// <summary>灰縫寬度 (feet)</summary>
        public double GroutWidthFeet => MmToFeet(GroutWidth);

        /// <summary>灰縫厚度 (feet)</summary>
        public double GroutThicknessFeet => MmToFeet(GroutThickness);

        /// <summary>一片磁磚含灰縫的水平間距 (feet)</summary>
        public double CellWidthFeet => TileWidthFeet + GroutWidthFeet;

        /// <summary>一片磁磚含灰縫的垂直間距 (feet)</summary>
        public double CellHeightFeet => TileHeightFeet + GroutWidthFeet;

        /// <summary>mm 轉換為 feet (Revit 內部單位)</summary>
        public static double MmToFeet(double mm)
        {
            return mm / 304.8;
        }

        /// <summary>feet 轉換為 mm</summary>
        public static double FeetToMm(double feet)
        {
            return feet * 304.8;
        }
    }
}
