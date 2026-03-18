# Changelog

All notable changes to the AntiGravity TilePlanner project will be documented in this file.

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
