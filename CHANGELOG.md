# Changelog

All notable changes to the AntiGravity TilePlanner project will be documented in this file.

## V4.3.4 (2026-03-20) - 📏 [對齊與穩定性修正] (ALIGNMENT STABILITY)
- **[精確對齊] 穩定網格原點**：重構網格生成算法，從 bb.Min 座標強制對齊，解決交丁排在邊緣產生的 1.5mm 精度落差。
- **[行列偵測] 幾何重心判定**：改用零件幾何重心 (Centroid) 進行行列劃分，取代不穩定的人造外框 (Bounding Box)，徹底解決門窗截斷處的規律失效問題。
- **[切割強化] 參考面延伸**：將網格參考面延伸長度從 1 呎加大至 5 呎，確保在大跨度牆面上也能 100% 穿透零件。

## V4.3.3 (2026-03-19) - 💎 [核心幾何引擎優化] (DIAMOND GEOMETRY ENGINE)
- **[重大突破] 造型控點對位延伸 (Proximity-Based Shape Extension)**：取代舊版「修改牆長」，直接操作磁磚造型控點。確保磁磚在轉角處先行「實心重疊」，從物理幾何層面徹底解決「平頭 (Flat Head)」現象。
- **[絕對穩定] nA + nB 向量邏輯**：正式導入「法向量相加」原理。利用 Revit 座標系天生的正負號特性，自動適應 2, 4, 8, 10 點鐘所有象限，達成 100% 的幾何穩定性。
- **[精確端面判定] Proximity Selection**：在延伸時會自動比對磁磚兩端與牆角的距離，確保磁磚僅向「轉角中心點」長出，杜絕反向伸長的錯誤。
- **[去牆化架構] Part-Centric Architecture**：所有運算聚焦於 Part 幾何，不更動主體牆 (Wall)，避免了 Revit 牆體接合引擎造成的座標漂移。

### 已知問題 (Known Issues)
- **[幾何補償] 1.52cm (0.05ft) 廢料延伸**：為確保轉角 Boolean 切割絕對成功，目前硬編碼了 0.05 呎的物理重疊量。這會導致被排除的「廢料零件」在視覺上比實際外表面長出 1.52cm。此為預期中的幾何穩定補償，暫不移除以維持切割成功率。

## V4.3.0 (2026-03-19) - 🏆 [穩定版本重要節點] (STABLE MILESTONE)
- **[重大架構升級] 階層式模組化 (Hierarchical Modularization)**：將專案正式升級為 V4.3，確立「模組化隔離」為程式開發的核心基準。
- **100% 邏輯繼承**：完全繼承 V4.1.21 最穩定的斜切物理與數學邏輯（包含 45 度角平分、Signed Distance 廢料判定），僅作結構搬移，確保零副作用。
- **三層式解耦**：
  - `Commands`：聚焦 Revit UI 按鈕與流程啟動。
  - `Services`：專責 Miter 處理、幾何切割與網格引擎 (`TilePartEngine`)。
  - `Utils`：集中底層 API 呼叫 (`RevitElementExtensions`) 與全域防護 (`RevitFailureHandlers`)。
- **Git 標記**：已建立 `V4.3.0-STABLE` 標籤，作為未來開發的絕對基準點。

## V4.1.21 (2026-03-18)

## [4.1.15] - 2026-03-18

### 4.1.15 新增功能

- **Isolated Transaction Loop**: 每個零件的切割均使用獨立交易 (Isolated Transaction)。若單一磁磚切割失敗，將自動 Rollback 並繼續處理其餘零件，大幅提升批次處理的成功率。
- **Local Z-Axis Support**: 透過取得磁磚主面的法向量 (Dominant Face Normal) 作為局部 Z 軸，完美支援斜屋頂、傾斜牆等任何非水平幾何的切割。
- **Point-to-Plane Waste Detection**: 廢料判定邏輯升級為點對平面 (Point-to-Plane) 頂點判定。只要零件超過 50% 的頂點位於切割面後方即視為廢料，解決了因重心偏離导致的誤判。
- **True Diagonal Calculation**: 直接計算內外表面的交點連線作為切割基準，消除不對稱厚度造成的角度偏差。

## [4.1.12] - 2026-03-18

### 4.1.12 新增功能

- **Absolute Normal Determination**: 退縮方向邏輯現在完全基於外表面法向量 (Absolute Normal)，解決了在複雜幾何（如 U 型或 L 型件）中位移方向不穩定的問題。
- **Zero-Z Precision**: 核心交點運算已降維至 2D XY 平面進行處理，消除了微小 Z 軸高程差导致的切割平面失效。
- **Auto Edge Detection**: 實現自動邊緣偵測，系統會掃描零件的頂部兩大垂直面以精確定位外角點，不再需要手動選擇表面。
- **Stability Guard**: 為確保 Revit 幾何核心穩定，強制灰縫下限為 3.5mm，防止因產生極微小廢料（< 1.6mm）而導致的切割崩潰。

## [4.1.11] - 2026-03-17

### 4.1.11 新增功能

- **Centroid-Based Retreat Selection**: In Miter Join mode, the 1mm gap retreat point is now selected based on its distance to the tile's centroid. This provides absolute stability across all geometric quadrants (2, 4, 8, 10 o'clock) as validated by vector addition logic.

### 4.1.11 修正項目

- **Quadrant Stability**: Resolved potential "reverse retreat" issues in specific quadrants where vector dot products could be ambiguous due to orientation.

## [4.1.10] - 2026-03-16

### 4.1.10 新增功能

- **2D Cramer's Rule Intersection**: Intersection point calculation is now projected to the XY plane, eliminating Z-axis "noise" from slight elevation differences.
- **Diagnostic Mode**: Silent cut failures were removed in favor of explicit `TaskDialog` warnings. If a cut fails, the system now explains which Part ID failed and possible reasons.
- **Relaxed Thickness Detection**: Improved outer-inner face matching tolerance to handle imperfect geometries.

## [4.1.9] - 2026-03-15

### 4.1.9 新增功能

- **Modular Join System**: Users can now explicitly choose between "Miter Join" (雙側切角) and "Butt Join" (蓋磚) via an interactive UI.
- **Dynamic Thickness Detection**: Butt Join mode automatically detects the covering tile's thickness to calculate precise retreat distances.

## [4.1.6] - 2026-03-15

### 4.1.6 新增功能

- **Silent Waste Handler**: Implemented `TinyPartFailureHandler` to automatically suppress and resolve Revit warnings caused by tiny slivers (<1.6mm) being deleted.

## [4.1.4] - 2026-03-14

### 4.1.4 新增功能

- **True Geometry Cutting**: Initial implementation of Cramer-based vertex calculations for corners.
- **BasisZ Knife**: Vertical extrusion logic for cutting planes to prevent wedge geometry errors.
- **Physical Adaptive Logic**: Automatic host tracking and part synchronization.
