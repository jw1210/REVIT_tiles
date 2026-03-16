# Changelog

All notable changes to the AntiGravity TilePlanner project will be documented in this file.

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
