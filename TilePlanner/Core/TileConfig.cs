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

        // ===== 雙向灰縫設定 =====

        /// <summary>水平灰縫寬度 (mm) — 磚縫的水平走向</summary>
        public double HGroutGap { get; set; } = 3;

        /// <summary>垂直灰縫寬度 (mm) — 磚縫的垂直走向</summary>
        public double VGroutGap { get; set; } = 3;

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

        /// <summary>水平灰縫寬度 (feet)</summary>
        public double HGroutGapFeet => MmToFeet(HGroutGap);

        /// <summary>垂直灰縫寬度 (feet)</summary>
        public double VGroutGapFeet => MmToFeet(VGroutGap);

        /// <summary>水平刀間距 = 磁磚高度 + 水平灰縫 (feet)</summary>
        public double HCellFeet => TileHeightFeet + HGroutGapFeet;

        /// <summary>垂直刀間距 = 磁磚寬度 + 垂直灰縫 (feet)</summary>
        public double VCellFeet => TileWidthFeet + VGroutGapFeet;

        // ===== 向下相容 =====

        /// <summary>灰縫寬度 (mm) — 向下相容, 設定時同步更新雙向</summary>
        public double GroutWidth
        {
            get => HGroutGap;
            set { HGroutGap = value; VGroutGap = value; }
        }

        /// <summary>向下相容：灰縫寬度 (feet)</summary>
        public double GroutWidthFeet => HGroutGapFeet;

        /// <summary>向下相容：CellWidthFeet</summary>
        public double CellWidthFeet => VCellFeet;

        /// <summary>向下相容：CellHeightFeet</summary>
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
