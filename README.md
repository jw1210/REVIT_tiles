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
│   ├── ManualCornerJoinCommand.cs  # (V4.3) UI 選擇與指令入口
│   └── ...
├── Core/
│   ├── TilePartEngine.cs           # 核心排磚引擎 (只管網格)
│   ├── GridConstraintManager.cs    # 剛體群組綁定機制
│   ├── Services/                   # (V4.3) 獨立業務邏輯模組
│   │   ├── MiterJoinService.cs     # 斜切分側排程
│   │   ├── WallGeometryService.cs  # 牆體準備與延伸
│   │   └── PartOperationService.cs # 切割與廢料隱藏
│   └── Utils/                      # (V4.3) 廣域防護與共用工具
│       ├── RevitElementExtensions.cs # 幾何與屬性萃取 (GetCentroid等)
│       └── RevitFailureHandlers.cs   # 警告靜默與錯誤吞噬者
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

### 階層式模組化開發原則 (Hierarchical Modularization)

本專案執行以下最高開發規範，確保程式碼的絕對穩定與防呆：

1.  **純結構解耦 (Structural Decoupling)**：所有的業務邏輯 (Services) 必須與 UI 選取 (Commands) 實體隔離。
2.  **單向依賴 (One-Way Dependency)**：`Commands` 呼叫 `Services`，`Services` 呼叫 `Utils`。下層絕不可反向呼叫上層。
3.  **純函數萃取 (Pure Functions)**：所有不涉及 Revit 交易狀態的 API 呼叫 (如取得重心、取得法向量)，必須寫作 `RevitElementExtensions` 下的靜態純函數。
4.  **變更防護牆 (Firewall against Regression)**：對任何單一功能 (如修改灰縫或排磚邏輯) 的修改，絕對不允許更動跨功能層的核心服務，確保 V4.1.21 的穩定幾何數學永不被意外污染。

---

**最後更新日期**: 2026-03-19 (V4.3 STABLE)
