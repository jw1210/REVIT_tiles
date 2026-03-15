# TilePlanner V3.5 — 幾何對位與視圖穩定技術指南

## 🛠️ 核心技術更新

### 1. 精準高度群組化 (Precision Height Grouping)
在 V3.4 之前，當牆面存在門窗開口時，水平切割產生的「橫條」會被門窗截斷成多個碎片。交丁算法 (Running Bond) 僅按零件順序進行 A-B 交替，導致門窗兩側的磁磚往往發生錯位（例如左側是 A 排，右側卻變成了 B 排）。

**解決方案：**
在 `TilePartEngine.PerformDivision` 中導入了基於 Y 軸（高度）的群組化邏輯：
- **中心點量測**：計算每個碎片零件的幾何中心點高度。
- **容差分組**：使用 `0.5 * CellHeightFeet` 作為容差。凡是高度差異在此範圍內的零件，不論是否被門窗隔開，一律視為「同一列」。
- **統一對位**：對「同一列」的所有零件統一套用相同的垂直分割網格（VA 或 VB），確保視覺上的對齊。

### 2. 強制元素隱藏 (Forced Element Visibility Control)
原有的網格切換邏輯依賴於類別整體的顯示控制，容易受到專案樣板或視圖設置的干擾，導致「綠色網格線」無法完全隱藏。

**優化內容：**
`ToggleGridCommand` 現在採用更底層的控制方式：
- **精準收集**：利用 `FilteredElementCollector` 配合名稱過濾 (`Contains("TileGrid_")`)。
- **實體控制**：直接使用 `activeView.HideElements` 與 `activeView.UnhideElements`。
- **狀態感知**：自動偵測第一條網格線的隱藏狀態，實現 100% 可靠的切換體驗。

---

## 🚦 部署與測試建議

### 部署路徑 (Revit 2025)
- **Addin**: `%AppData%\Autodesk\Revit\Addins\2025\TilePlanner.addin`
- **DLL**: `%AppData%\Autodesk\Revit\Addins\2025\TilePlanner\TilePlanner.dll`

### 測試場景
1. **交丁連貫性測試**：
   - 選擇一面有大型落地窗的牆。
   - 執行「交丁排」計畫。
   - 檢查窗戶左、右、上方的磁磚縫隙是否完全對齊。
2. **網格開關壓力測試**：
   - 在複雜的 3D 視圖中切換「顯示/隱藏 網格」。
   - 確認所有 `TileGrid_` 系列的參考平面均能瞬間消失/出現。

---

## 📋 開發者備註
V3.5 標誌著 TilePlanner 從「單純分割」進化到了「感知幾何佈局」的新階段。高度群組化邏輯為未來更複雜的圖案排列（如人字拼、隨機拼）奠定了穩固的基礎。
