# TilePlanner V3.6 技術指南 — 全域網格控制版

## 1. 網格檢索機制修正

在 V3.6 中我們解決了隱藏參考平面後無法搜尋到的問題：

- **現象描述**：當使用者在 Revit 中隱藏元素時，`FilteredElementCollector` 若只搜尋「當前視圖」會無法找到已被隱藏的對象。
- **解決方案**：
  - 擴大搜尋 `doc` 全域文件。
  - 使用 `rp.IsHidden(activeView)` 進行後置過濾。

## 2. 佈署模式修正

本版本開始採用「指針式佈署」：

- **路徑**：`C:\Users\jenwe\AppData\Roaming\Autodesk\Revit\Addins\2025\TilePlanner.addin`。
- **配置**：`<Assembly>` 標籤直接指向桌面專案路徑，不需手動複製 DLL 至 AppData。

## 3. 切換邏輯精讀

為了符合直覺操作：

- 如果視圖中有任何一片網格是可見的 -> **全部隱藏**。
- 如果視圖中所有網格都已隱藏 -> **全部開啟**。
- 這避免了網格數過多時個別隱藏造成的混亂。
