# AntiGravity 磁磚計畫 (TilePlanner) — V2.8 零件切換技術指南

## 新增功能：零件顯示切換 (Parts Visibility Toggle)

**V2.8** 引入了視圖控制自動化，解決了 Revit 原生屬性面板操作繁瑣的問題。

---

## 技術實作

### 1. 視圖狀態切換 (Visibility Toggle)
- **類別**: `TogglePartsVisibilityCommand.cs`
- **邏輯**: 
  ```csharp
  if (activeView.PartsVisibility == PartsVisibility.ShowOriginalOnly)
      activeView.PartsVisibility = PartsVisibility.ShowPartsOnly;
  else
      activeView.PartsVisibility = PartsVisibility.ShowOriginalOnly;
  ```
- **安全性**: 加入 `view.ViewType` 判斷，僅在 3D、平面、剖面、立面視圖中啟用，防止在明細表視圖執行導致報錯。

### 2. Ribbon UI 擴充
- **位置**: `App.cs`
- **組件**: `PushButton`
- **特點**: 採用雙行標題「顯示/隱藏\n零件」，優化面板空間利用。

---

## V2.7+ 核心特性保留
- **一鍵重繪 (Destroy & Recreate)**: 完美適應最新幾何。
- **邊緣容差 (2mm Edge Tolerance)**: 磁磚邊界不縮水。

---

**更新日期**: 2026-03-14
