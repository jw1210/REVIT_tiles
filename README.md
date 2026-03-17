# AntiGravity 磁磚計畫 (TilePlanner) — Revit 專家級排磚外掛

## 🚀 V4.1.12 自動化核心 — 轉角偵測與 3.5mm 穩定版

**版本 V4.1.12 (2026-03-17)** 導入全自動幾何識別技術與法向量絕對判定邏輯：

- ✨ **法向量絕對判定 (Absolute Normal)** — 直接依據外表面法向量決定退縮方向，完美適配 U 型、L 型等各類異型件。
- ✨ **自動邊緣偵測 (Auto Edge Detection)** — 系統自動掃描零件主面並定位絕對外角點，不需手動點選表面。
- ✨ **3.5mm 穩定切割 (Stability Guard)** — 強制灰縫下限，確保廢料體積大於 Revit 核心限制，防止 API 崩潰。
- ✨ **零高程干擾 (Zero-Z Precision)** — 核心交點運算降維至 2D 平面，徹底消除微小高程差導致的切割失敗。
- ✨ **全本土化專業介面** — 繁體中文專業術語與操作引導。

## 核心功能特色

本外掛採用獨創的**兩階段參照平面切割法 (Two-Stage Reference Plane Division)**。

- ✅ **基於零件 (Parts) 架構**：不依賴沉重的帷幕牆，效能極致。
- ✅ **全物件自動切割**：自動追蹤宿主主體並同步完成整面磁磚分割。
- ✅ **全向度幾何切割**：支援 45 度磨角及各類異型收頭。
- ✅ **外參開口自動閃避**：偵測交疊的外參模型門窗並剔除廢料。
- ✅ **全版本支援**：相容 Revit 2024 與 2025。

## 專案核心結構

```text
TilePlanner/
├── App.cs                          # Ribbon UI 入口
├── Commands/
│   ├── CreateTilePlanCommand.cs    # 建立 Master Transaction
│   ├── ManualCornerJoinCommand.cs  # V4.1 收邊與幾何補償引擎
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
- **收邊處理**：點擊「手動轉角接合」，選取兩側磁磚後設定形式與灰縫 (建議 2mm 以上)。

## 🛠️ 開發規範與核心架構

### 模組化開發原則 (Modular Specification)

為了確保程式碼的穩定性，本專案執行以下開發規範：

1.  **策略模式分流 (Strategy Pattern)**：新功能或接合形式必須實作為獨立方法或類別。
2.  **邏輯與核心隔離**：算法邏輯與 Revit API 切割流程物理分離。
3.  **單一職責原則**：確保各 Method 職責明確，禁混入不相關邏輯。

---

**最後更新日期**: 2026-03-17
