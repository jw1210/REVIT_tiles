# AntiGravity 磁磚計畫 (TilePlanner) — Revit 專家級排磚外掛

## 🚀 V4.3.3 鑽石幾何引擎 — 造型控點對位與 nA+nB 向量穩定版

**版本 V4.3.3 (2026-03-19)** 導入革命性的「零件中心 (Part-Centric)」延伸技術與絕對平分角向量邏輯：

- 💎 **造型控點對位延伸 (Shape-Handle Extension)** — 直接操作磁磚零件的造型控點，取代修改牆長。確保磁磚在轉角處先行「實心重疊」，物理性根除平頭現象。
- 💎 **nA + nB 絕對平分向量** — 利用表面法向量相加原理，自動適配東、西、南、北所有座標象限，無須 If-Else 判定。
- 💎 **座標鄰近端面選擇 (Proximity Selection)** — 智能比對磁磚端面與轉角的座標距離，確保磁磚僅向轉角側生長。
- 💎 **跨版本穩定支援** — 全面相容 Revit 2024 (.NET 4.8) 與 Revit 2025 (.NET 8)。

## 核心功能特色

本外掛採用獨創的**三階段零件幾何核心 (Triple-Stage Part Geometry Core)**。

- ✅ **精確背斜 (Miter Join)**：兩側磁磚自動延伸重疊，並執行精確的 45 度 nA + nB 向量切割。
- ✅ **基於零件 (Parts) 架構**：不依賴牆體接合引擎，效能極致且座標穩定。
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

**最後更新日期**: 2026-03-19 (V4.3.3 DIAMOND)
