# TilePlanner V2.8 — 零件顯示切換部署指南

## 📦 部署步驟

### Step 1: 編譯專案
```powershell
cd c:\Users\jenwe\Desktop\REVIT_tiles
dotnet build TilePlanner.sln /p:Configuration=Release /p:Platform="Any CPU"
```

### Step 2: 派送外掛
```powershell
$addinsPath = "$($env:AppData)\Autodesk\Revit\Addins\2025"
Copy-Item "TilePlanner\bin\Release\net8.0-windows\TilePlanner.dll" -Destination $addinsPath -Force
Copy-Item "TilePlanner.addin" -Destination $addinsPath -Force
```

---

## 🧪 V2.8 驗證點

### 1. 一鍵切換零件顯示 (Toggle Visibility)
- **測試方式**: 在畫面上點擊新按鈕「顯示/隱藏零件」。
- **預期結果**: 
  - 狀態 A: 看到完整的牆（Show Original）。
  - 狀態 B: 看到切好的磁磚零件（Show Parts）。
  - 點擊按鈕應能流暢循環切換。

### 2. 功能繼承 (Legacy Features)
- **預期結果**: 「建立磁磚計畫」、「移除磁磚計畫」與「整體連動」功能運作如常。

---

## 📋 部署清單

- [x] V2.8 代碼編譯成功
- [x] 新按鈕已出現在 Ribbon
- [x] 驗證切換功能有效
- [x] 確保不影響現有重繪機制
