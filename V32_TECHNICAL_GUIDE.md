# AntiGravity 磁磚計畫 (TilePlanner) — V3.2 技術規格指南

## 核心演進：幾何生命週期同步 (Geometry Life-cycle Sync)

**V3.2** 解決了 Revit API 中幾何快取導致的非同步問題，確保了在物件形狀變動後的「零延遲」重繪。

---

## 技術細節

### 1. Geometry-Regen Precedence (幾何刷新優先權)
- **問題**: Revit 的 `get_Geometry` 方法在物件變動後若未經 `Regenerate()`，可能返回該 Transaction 開始時的舊快取。
- **解法**: 
  - 步驟 A: 執行 `_doc.Delete(makersToDelete)`。
  - 步驟 B: 立即調用 `_doc.Regenerate()`。
  - 步驟 C: 此時再調用 `GetTargetFace()` 獲取邊界。
- **結果**: 獲取的物理參數（BoundingBoxXYZ/UV）與當前視窗內的真實尺寸 100% 同步。

### 2. Multi-Regen Strategy (多重刷新策略)
- **Regen 1**: 在獲取幾何前（清除舊零件後）。
- **Regen 2**: 在建立網格後（使 ReferencePlane 生成有效面）。
- **Regen 3**: 在建立標註約束後（確保網格鎖定關係已穩固）。
- **Regen 4**: 在最終分割後（刷新零件可見性）。

### 3. Error Tolerance Maintenance (持續容差維護)
- 依然保留 V3.1 的 `WarningSwallower` 全域靜默機制與 2mm 邊界保護。

---

**更新日期**: 2026-03-14
