# AntiGravity 磁磚計畫 (TilePlanner) — Revit 零件排磚專家

## 🚀 V4.1.12 自動化核心 — 轉角偵測與 3.5mm 穩定版

**版本 V4.1.12 (2026-03-17)** 導入全自動幾何識別技術：

- ✨ **自動邊緣偵測 (Auto Edge Detection)** — 系統自動掃描零件主面並定位絕對外角點，不需手動點選表面。
- ✨ **3.5mm 穩定切割 (Stability Guard)** — 強制灰縫下限，確保廢料體積大於 Revit 核心限制，防止 API 崩潰。
- ✨ **內向偏移算法 (Inward Offset)** — 透過向量內積判斷退縮方向，完美克服牆體接合處的幾何干擾。
- ✨ **全本土化專業介面** — 完整繁體中文專業術語與操作引導。

## 核心功能特色

本外掛採用獨創的**兩階段參照平面切割法 (Two-Stage Reference Plane Division)**。

- ✅ **基於零件 (Parts) 架構**：不依賴沉重的帷幕牆，效能極致。
- ✅ **全物件自動切割**：自動追蹤宿主主體並同步完成整面磁磚分割。
- ✅ **全向度幾何切割**：支援 45 度磨角及各類異型收頭。
- ✅ **外參開口自動閃避**：偵測交疊的外參模型門窗並剔除廢料。
- ✅ **全版本支援**：完美相容 Revit 2024 與 2025。

## 專案核心結構

```text
TilePlanner/
├── App.cs                          # Ribbon UI 入口
├── Commands/
│   ├── CreateTilePlanCommand.cs    # 建立 Master Transaction
│   ├── ManualCornerJoinCommand.cs  # [NEW] V4.1 收邊與幾何補償引擎
│   ├── RemoveTilePlanCommand.cs    # 追查 Host 並刪除
│   └── TogglePartsVisibilityCommand.cs # 全域視圖切換
├── Core/
│   ├── TilePartEngine.cs           # 核心引擎 (物理切割與 Regen)
│   └── GridConstraintManager.cs    # 剛體群組綁定機制 (防呆平移)
├── UI/
│   └── TilePlannerDialog.cs        # 主程式對話框
```

## 使用方法

### 1. 安裝

1. 將 `TilePlanner.addin` 複製到：
   ```text
   %AppData%\Autodesk\Revit\Addins\2024 (或 2025)\
   ```
2. 將對應版本的 `TilePlanner.dll` 複製到相同目錄。

### 2. 操作說明

- **基本排磚**：選取物件後點擊「磁磚計畫」，調整參數後執行。
- **收邊處理**：點擊「手動轉角收邊」，選取兩側磁磚後設定形式與灰縫 (建議 2mm 以上)。

---

**最後更新日期**: 2026-03-16
