# TilePlanner V3.8 技術指南 — 核心補強與 UX 提升

## 1. UX 直覺化：Enter 確認流程

在 V3.7 中，許多使用者選取零件後不清楚需要按下 Revit 選項列的「完成」或鍵盤的 **Enter**。

V3.8 正式優化此流程：

- **提示語更新**：所有多重選取指令均包含 `(選取完成請按 Enter 鍵確認)`。
- **對應指令**：`建立磁磚計畫 (批次)`、`局部轉角收邊`。

## 2. 強韌交易保護 (Robust Transaction Model)

為了防止在複雜幾何切割失敗時導致 Revit 狀態混難或 UI 無反應，V3.8 導入了全新的交易結構：

### TransactionGroup 架構

1. **隱形的容器 (TransactionGroup)**：負責封裝整個轉角邏輯。
2. **階段式子交易 (Sub-Transactions)**：
   - **合併交易 (tMerge)**：先行完成零件鎔鑄。
   - **幾何刷新**：強制 `doc.Regenerate()` 確保實體已生成。
   - **切割交易 (tCut)**：執行最終的 3D 投影切割。
3. **錯誤自動彈窗**：
   - 取消了以往的靜默攔截 (Empty Catch)。
   - 當幾何不滿足切割條件（如：面不垂直、面積過小）時，系統會精準彈出原因，協助使用者排除障礙。

## 3. 部署資訊 (V3.8)

- **目錄**：`C:\Users\jenwe\AppData\Roaming\Autodesk\Revit\Addins\2025\TilePlanner_V38\`。
- **配置**：`.addin` 檔案已完成切換，確保載入的是 V3.8 全新補強核心。
