# AntiGravity 磁磚計畫 (TilePlanner) — Revit 零件排磚專家

## 🚀 V4.1.4 物理自適應版 — 真實幾何切割與克拉瑪頂點交會

**版本 V4.1.4 (2026-03-16)** 達成不需要延伸補償的技術突破：

- ✨ **真實幾何切割 (True Geometry Cut)** — 只要磁磚零件有物理接觸，系統即可自動完成精準收頭，不需額外執行 50mm 延伸。
- ✨ **克拉瑪頂點定位 (Cramer Vertex)** — 利用線性代數公式精確定義轉角外緣交點，確保切刀面完美校準。
- ✨ **垂直雷射刀 (BasisZ Knife)** — 強制垂直貫穿路徑，徹底防止產生導致 Revit 報錯的水平楔形碎片。
- ✨ **物理自適應邏輯** — 自動識別 A/B 側零件重心並配對切刀偏移，防止交叉錯切。
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
