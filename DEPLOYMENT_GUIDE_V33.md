# TilePlanner V3.3 — UI 交互優化部署指南

## 📦 部署步驟

### Step 1: 關閉 Revit 2025
- **必備**: 確保 Revit 完全關閉。

### Step 2: 編譯專案
```powershell
dotnet build TilePlanner.sln /p:Configuration=Release /p:Platform="Any CPU"
```

### Step 3: 佈署 DLL
```powershell
$addinsPath = "$($env:AppData)\Autodesk\Revit\Addins\2025"
$subPath = "$addinsPath\TilePlanner"
Copy-Item "TilePlanner\bin\Release\net8.0-windows\TilePlanner.dll" -Destination "$subPath\TilePlanner.dll" -Force
```

---

## 🧪 V3.3 驗證點

### 1. UI 佈局檢查
- **操作**: 啟動「建立磁磚計畫」彈窗。
- **預期**: 
  - 在「寬度/高度」入力框右側看到「🔄 對調長寬」按鈕。
  - 在「水平/垂直灰縫」入力框右側看到「🔄 對調灰縫」按鈕。

### 2. 功能獨立性測試
- **測試 A**: 點擊「🔄 對調長寬」。
  - **預期**: 僅切換尺寸，灰縫寬度保持不變。
- **測試 B**: 點擊「🔄 對調灰縫」。
  - **預期**: 僅切換縫隙，磁磚尺寸保持不變。

---

## 📋 最終檢查表
- [x] TilePlannerDialog.cs：實作 Split Swap 邏輯
- [x] UI 佈局：採用 4 欄式 Input Grid
- [x] V3.3 文檔更新完成
