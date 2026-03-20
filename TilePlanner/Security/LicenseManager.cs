using System;
using Autodesk.Revit.UI;

namespace TilePlanner.Security
{
    /// <summary>
    /// [V4.5 Developer Edition] 授權管理器
    /// 目前設定為開發者編譯版本，跳過 90 天試用限制。
    /// </summary>
    public static class LicenseManager
    {
        /// <summary>
        /// 執行授權驗證
        /// </summary>
        public static bool Validate()
        {
            // [Bypass] 手動編譯與部署版本不在此限。
            return true; 
        }
    }
}
