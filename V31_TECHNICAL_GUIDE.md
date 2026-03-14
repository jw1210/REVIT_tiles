# AntiGravity 磁磚計畫 (TilePlanner) — V3.1 技術規格指南

## 核心演進：單一交易與全域靜默

**V3.1** 拋棄了傳統的交易群組，改用更激進的單一交易策略來克服 Revit API 的限制。

---

## 技術細節

### 1. Master Transaction (主交易)
- **架構優化**: 取代 `TransactionGroup`。
- **優勢**: 
  - 支援 `IFailuresPreprocessor`。
  - 確保所有原子操作（零件生成、網格更新、視圖切換）共用同一個失敗預處理器。
  - 單一提交點，避免 Revit 在交易群組結束時「爆破式」彈出警告。

### 2. WarningSwallower (警告吞噬者)
- **邏輯**: 繼承 `IFailuresPreprocessor`，在 `PreprocessFailures` 中執行迴圈檢索。
- **處理**: 只要 `Severity == Warning`，直接調用 `failuresAccessor.DeleteWarning(f)`。
- **結果**: 100% 遮蔽「零件未相交」、「幾何微小偏差」等 Revit 非致命報錯。

### 3. Universal Visibility Control (全域可見性控制)
- **機制**: 透過 `BuiltInParameter.VIEW_PARTS_VISIBILITY` 直攻底層參數。
- **兼容性**: 完美支持 3D 視圖、剖面圖、立面圖。
- **強健性**: 加入 `IsReadOnly` 偵測，友善提示視圖樣板 (View Template) 鎖定狀態。

---

**更新日期**: 2026-03-14
