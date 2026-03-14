# AntiGravity 磁磚計畫 (TilePlanner) — V3.4 技術規格指南

## 核心演進：UX 邏輯加固與交互優化 (Logic Hardening)

**V3.4** 專注於解決使用者交互中的潛在崩潰點，透過嚴格的欄位驗證與鍵盤快速鍵支援，提供工程級的軟體穩定性。

---

## 技術細節

### 1. Robust Data Validation (強健數據檢核)
- **機制**: 捨棄 `double.Parse`，全面改用 `double.TryParse`。
- **邏輯分級**:
    - **磁磚尺寸**: 必須為正數 (Value > 0)。若輸入無效或 <= 0 則攔截。
    - **灰縫寬度**: 允許為 0 (無灰縫拼貼)，但不可為負數 (Value >= 0)。
- **交互補償**: 當檢核失敗時，Dialog 不會關閉。程式會自動彈出警示視窗，並透過 `Focus()` 與 `SelectAll()` 將游標精確帶回報錯的欄位，達成「零阻力修正」。

### 2. UI State Dynamic Context (動態介面上下文)
- **引導式設計**: 排列模式 (RadioButton) 與交丁偏移面板 (pnlOffset) 實作物理連動。
- **邏輯**:
    - 選中「交丁排」時，面板 `Visibility = Visible`。
    - 選中「正排」時，面板 `Visibility = Collapsed`。
- **效益**: 減少不必要的控制項佔用視覺空間，防止使用者在正排模式下誤調整偏移參數。

### 3. Split-Axis Swapping (雙軸對調邏輯)
- **解耦設計**: 將「長寬對調」與「灰縫對調」徹底分離為 `BtnSwapSize_Click` 與 `BtnSwapGrout_Click`。
- **佈局優化**: 採用 4 欄式 Input Grid，將 `🔄` 按鈕緊鄰數據源，符合費茨法則 (Fitts's Law)，縮短滑鼠移動距離。

### 4. Accessibility & Productivity (快捷鍵與生產力)
- **屬性綁定**: 
    - 確定按鈕 (`IsDefault=true`) -> 綁定鍵盤 `Enter`。
    - 取消按鈕 (`IsCancel=true`) -> 綁定鍵盤 `Esc`。
- **效益**: 符合專業 BIM 建模師的鍵盤優先操作習慣。

---

**更新日期**: 2026-03-14
