namespace TilePlanner.Core
{
    /// <summary>
    /// 磁磚與灰縫的參數配置 (V2.2 雙向灰縫版)
    /// </summary>
    public class TileConfig
    {
        // ===== 磁磚設定 =====

        /// <summary>磁磚寬度 (mm)</summary>
        public double TileWidth { get; set; } = 200;

        /// <summary>磁磚高度 (mm)</summary>
        public double TileHeight { get; set; } = 200;

        // ===== 灰縫設定 (V3.2 強化雙向版) =====

        /// <summary>水平灰縫寬度 (mm)</summary>
        public double HGroutGap { get; set; } = 3;

        /// <summary>垂直灰縫寬度 (mm)</summary>
        public double VGroutGap { get; set; } = 3;

        /// <summary>同步用灰縫寬度 (mm) — 設定時同步更新雙向</summary>
        public double GroutWidth
        {
            get => HGroutGap;
            set { HGroutGap = value; VGroutGap = value; }
        }

        // ===== 排列模式 =====

        /// <summary>排列模式</summary>
        public TilePatternType PatternType { get; set; } = TilePatternType.Grid;

        /// <summary>
        /// 交丁偏移百分比 (0.0 ~ 1.0)
        /// </summary>
        public double RunningBondOffset { get; set; } = 0.5;

        // ===== 轉換輔助方法 =====

        /// <summary>磁磚寬度 (feet)</summary>
        public double TileWidthFeet => MmToFeet(TileWidth);

        /// <summary>磁磚高度 (feet)</summary>
        public double TileHeightFeet => MmToFeet(TileHeight);

        /// <summary>水平灰縫寬度 (feet)</summary>
        public double HGroutGapFeet => MmToFeet(HGroutGap);

        /// <summary>垂直灰縫寬度 (feet)</summary>
        public double VGroutGapFeet => MmToFeet(VGroutGap);

        /// <summary>水平刀間距 = 磁磚高度 + 水平灰縫 (feet)</summary>
        public double HCellFeet => TileHeightFeet + HGroutGapFeet;

        /// <summary>垂直刀間距 = 磁磚寬度 + 垂直灰縫 (feet)</summary>
        public double VCellFeet => TileWidthFeet + VGroutGapFeet;

        // ===== 向下相容屬性 (保持內部一致性) =====
        public double GroutWidthFeet => HGroutGapFeet;
        public double CellWidthFeet => VCellFeet;
        public double CellHeightFeet => HCellFeet;

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
