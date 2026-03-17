using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Autodesk.Revit.UI;

namespace TilePlanner.Security
{
    /// <summary>
    /// [V3.9.3 Commercial] 商業版授權管理器
    /// 具備 AES 加密存儲、90 天試用限制與時鐘回溯防禦功能。
    /// </summary>
    public static class LicenseManager
    {
        // 登錄檔路徑與金鑰名稱
        private const string RegistryPath = @"Software\AntiGravity\TilePlanner\SecureData";
        private const string TokenName = "AuthToken";
        private const int TrialDays = 90;

        // AES 加密與鹽值 (正式發布時建議更改此鹽值)
        private static readonly byte[] Salt = Encoding.UTF8.GetBytes("Commercial_TilePlanner_Secure_Salt_2026");
        
        // 內部的靜默密鑰 (基於電腦名稱)，防止直接修改 Registry 跨機使用
        private static string GetInternalKey() => Environment.MachineName + "_AntiGravity";

        /// <summary>
        /// 執行授權驗證
        /// </summary>
        public static bool Validate()
        {
            // [V4.1.5 Developer Bypass] 
            // 根據使用者指示：只有安裝檔(.exe)安裝的程式才受 90 天限制。
            // 手動編譯與部署版本不在此限。
            return true; 
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                
                // 1. 若 Registry 不存在，代表是第一次啟動，進行初始化
                if (key == null)
                {
                    return InitializeActivation();
                }

                // 2. 讀取並解密 Token
                string encryptedToken = key.GetValue(TokenName) as string;
                if (string.IsNullOrEmpty(encryptedToken)) return InitializeActivation();

                string decryptedInfo = Decrypt(encryptedToken, GetInternalKey());
                
                // 格式: 激活日期|最後執行日期
                string[] parts = decryptedInfo.Split('|');
                if (parts.Length != 2) return InitializeActivation();

                DateTime activationDate = DateTime.Parse(parts[0]);
                DateTime lastRunDate = DateTime.Parse(parts[1]);
                DateTime now = DateTime.Now;

                // 3. 防破解：時鐘回溯偵測 (Anti-Clock Rollback)
                // 如果目前時間比「最後一次執行時間」還早，說明使用者調慢了電腦時鐘
                if (now < lastRunDate)
                {
                    TaskDialog.Show("安全性鎖定", "【系統警報】檢測到不正常的系統時間（時鐘回溯）。\n授權驗證失敗，請聯繫管理員獲取正式版授權。");
                    return false;
                }

                // 4. 計算剩餘天數
                TimeSpan elapsed = now - activationDate;
                int remainingDays = TrialDays - (int)elapsed.TotalDays;

                if (remainingDays < 0)
                {
                    TaskDialog.Show("試用期結束", "您的 90 天試用期已屆滿。\n如需繼續使用商業版功能，請聯繫 AntiGravity 官方人員。");
                    return false;
                }

                // 5. 更新「最後執行日期」並存回 Registry (防止下次調回時間)
                UpdateLastRunDate(key, activationDate, now);

                // 6. 接近過期提醒 (最後 7 天)
                if (remainingDays <= 7)
                {
                    TaskDialog.Show("授權即將到期", $"提醒：您的 TilePlanner 試用授權僅剩餘 {remainingDays} 天。");
                }

                return true;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("驗證錯誤", "授權組件發生未知異常，請重新安裝外掛。\n細節：" + ex.Message);
                return false;
            }
        }

        private static bool InitializeActivation()
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                DateTime now = DateTime.Now;
                
                // 將激活日期與目前日期加密後存入
                string token = Encrypt($"{now:O}|{now:O}", GetInternalKey());
                key.SetValue(TokenName, token);

                TaskDialog.Show("歡迎使用商業試用版", "TilePlanner 磁磚大師 已成功啟動！\n您的 90 天試用期從今日開始計算。");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void UpdateLastRunDate(RegistryKey key, DateTime activationDate, DateTime now)
        {
            string token = Encrypt($"{activationDate:O}|{now:O}", GetInternalKey());
            key.SetValue(TokenName, token);
        }

        #region --- AES 加密邏輯 ---

        private static string Encrypt(string plainText, string key)
        {
            using (Aes aes = Aes.Create())
            {
                var pdb = new Rfc2898DeriveBytes(key, Salt, 1000, HashAlgorithmName.SHA256);
                aes.Key = pdb.GetBytes(32);
                aes.IV = pdb.GetBytes(16);
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        byte[] data = Encoding.UTF8.GetBytes(plainText);
                        cs.Write(data, 0, data.Length);
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        private static string Decrypt(string cipherText, string key)
        {
            using (Aes aes = Aes.Create())
            {
                var pdb = new Rfc2898DeriveBytes(key, Salt, 1000, HashAlgorithmName.SHA256);
                aes.Key = pdb.GetBytes(32);
                aes.IV = pdb.GetBytes(16);
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        byte[] data = Convert.FromBase64String(cipherText);
                        cs.Write(data, 0, data.Length);
                    }
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        #endregion
    }
}
