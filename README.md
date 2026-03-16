# AntiGravity 磁磚計畫 (TilePlanner) — Revit 專家級排磚外掛

## 🚀 V4.1.11 磨角穩定版 — 全向度幾何切割與高精度診斷

**版本 V4.1.11 (2026-03-17)** 進一步強化了複雜轉角的穩定性與診斷能力：

- ✨ **全角象限適配 (Refined Miter Join)** — 採用「重心距離判定法」執行灰縫退縮，完美適配 2時、4時、8時、10時方向等全向度轉角，確保 1mm 灰縫絕對向內退縮。
- ✨ **零高程干擾 (Zero-Z Precision)** — 核心交點運算採用降維升級 (2D Cramer's Rule)，徹底消除 Z 軸微小高程不一致導致的切割失敗。
- ✨ **主動診斷模式 (Diagnostic Mode)** — 當幾何條件不滿足切割時，會彈出詳細對話框顯示零件 ID 與失敗原因，不再靜默失敗。
- ✨ **模組化分流 (Strategy Pattern)** — 支援「雙側切角」(Miter) 與「蓋磚」(Butt) 雙模式，且具備自動厚度辨識功能。
- ✨ **絕對靜默失敗處理** — 內部掛載 TinyPartFailureHandler，自動消除低於 1.6mm 的極微小零件刪除警告。

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

## 🛠️ 開發規範與核心架構

### 模組化開發原則 (Modular Specification)

為了確保程式碼的穩定性並避免「牽一髮而動全身」的誤修改，本導案強制執行**高度模組化**開發流程：

1.  **策略模式分流 (Strategy Pattern)**：任何新功能或接合形式 (Join Type) 必須實作為獨立的 Method 或 Class，並透過 UI 入口進行分流呼叫。
2.  **邏輯與核心隔離**：算法邏輯必須與 Revit API 切割流程 (ExecutePlanViewCut) 物理分離。
3.  **單一職責原則**：
    -   `ExecuteMiterJoin` 僅處理雙側 45 度邏輯。
    -   `ExecuteButtJoin` 僅處理蓋磚與厚度補償邏輯。
    -   禁止將新邏輯直接混入既有的 Method 中。

---

**最後更新日期**: 2026-03-17
