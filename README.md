# AntiGravity 磁磚計畫 (TilePlanner) — Revit 零件排磚專家

## 🎯 V3.4 版本亮點 — UX 邏輯加固版

**版本 V3.4 (2026-03-14)** 完善了操作細節與穩定性：

- ✨ **輸入防護網 (Anti-Crash)** — 100% 攔截格式錯誤，杜絕因非法字元導致的崩潰。
- ✨ **快速鍵支持 (Keyboard Map)** — 支援 Enter 執行與 Esc 取消，提升批量操作效率。
- ✨ **獨立對調功能 (Split Swap)** — 尺寸長寬與灰縫現在可獨立對調，操作更直覺。
- ✨ **一鍵即時更新 (Instant Sync)** — 解決幾何延遲，完美對齊最新物理邊界。

## 核心功能特色

本外掛採用獨創的**兩階段參照平面切割法 (Two-Stage Reference Plane Division)**。

- ✅ **基於零件 (Parts) 架構**：不依賴沉重的帷幕牆，效能極致。
- ✅ **100% 靜默執行**：全面攔截「零件未相交」等警告，永不彈出 HTML 錯誤報告。
- ✅ **全物件自動切割**：自動追蹤宿主主體並同步完成整面磁磚分割。
- ✅ **外參開口自動閃避**：偵測交疊的外參模型門窗並剔除廢料。
- ✅ **全視圖支援**：完美支援 3D、平面、剖面等所有視角。

## 專案核心結構

```text
TilePlanner/
├── App.cs                          # Ribbon UI 入口
├── Commands/
│   ├── CreateTilePlanCommand.cs    # 建立 Master Transaction
│   ├── RemoveTilePlanCommand.cs    # 追查 Host 並刪除
│   └── TogglePartsVisibilityCommand.cs # 全域視圖切換 (支援 3D)
├── Core/
│   ├── TilePartEngine.cs           # 核心引擎 (物理切割與 Regen)
│   └── GridConstraintManager.cs    # 隱形標註鎖定機制
├── UI/
│   └── TilePlannerDialog.cs        # 純 C# WPF 對話框 (含防呆驗證)
```

## 使用方法

### 1. 安裝

1. 將 `TilePlanner.addin` 複製到：
   ```text
   %AppData%\Autodesk\Revit\Addins\2025\
   ```
2. 將編譯輸出的 `TilePlanner.dll` 複製到相同目錄。

### 2. 操作說明

- **建立/重繪**：選取物件後點擊「磁磚計畫」，調整參數後按確定。
- **顯示切換**：使用 Ribbon 上的獨立按鈕切換網格或零件顯示。

---

**更新日期**: 2026-03-14
