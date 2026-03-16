# AntiGravity 磁磚計畫 (TilePlanner) — Revit 零件排磚專家

## 🚀 V4.1.2 最終優化版 — 終極收邊與幾何補償

**版本 V4.1.2 (2026-03-16)** 穩定的幾何處理與專業收邊邏輯：

- ✨ **終極幾何補償 (Geometric Compensation)** — 完美支援非 90 度轉角、圓弧牆面及各類複雜地形。
- ✨ **四大收邊形式** — 支援 Miter (磨角)、Cover (蓋磚)、Butt (離縫)、Embed (嵌入)。
- ✨ **正面蓋磚「雙向齊平」優化** — 獨創雙向刀法，確保蓋磚側與被切側 100% 齊平平整。
- ✨ **2mm 灰縫安全限制** — 強制執行 2mm 下限，徹底杜絕 Revit 生成微小細屑導致的系統崩潰。
- ✨ **失敗預處理器 (Failure Resolver)** — 自動攔截並修復 Revit 內部錯誤，實現交易 0 彈窗。

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
