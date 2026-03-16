# AntiGravity 磁磚計畫 (TilePlanner) — Revit 零件排磚專家

## 🚀 V4.1.4 專業修正版 — 垂直雷射刀與穩定性優化

**版本 V4.1.4 (2026-03-16)** 針對轉角接合幾何穩定性進行深度優化：

- ✨ **垂直雷射刀 (Vertical Laser Knife)** — 將切割軸線強制修正為垂直向 (`XYZ.BasisZ`)，確保刀刃絕對貫穿磁磚，解決水平碎屑導致的零件刪除錯誤。
- ✨ **零件物理延伸 (Part Extension Optimization)** — 整合 50mm 預延伸邏輯，搭配主體同步機制，確保分割向量與零件實體 100% 交會。
- ✨ **智慧廢料清理 (Smart Garbage Collection)** — 優化廢料識別演算法，利用重心投影距離精準刪除延伸段，保留完美斷面。
- ✨ **全本土化介面 (Localized UI)** — 轉角按鈕、設定對話框及操作提示完整繁體中文化。
- ✨ **2mm 灰縫物理限制** — 延續 V4.1.2 嚴謹標準，防止生成極小切屑以確保 Revit 穩定運作。

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
