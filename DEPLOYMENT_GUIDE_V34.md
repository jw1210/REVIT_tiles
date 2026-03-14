# TilePlanner V3.4 — UX 邏輯加固部署指南

## 📦 部署步驟

### Step 1: 關閉 Revit 2025
- **重要**: 確保 Revit 完全關閉。

### Step 2: 編譯專案
```powershell
dotnet build TilePlanner.sln /p:Configuration=Release /p:Platform="Any CPU"
```

### Step 3: 更新 DLL
```powershell
$addinsPath = "$($env:AppData)\Autodesk\Revit\Addins\2025"
$subPath = "$addinsPath\TilePlanner"
Copy-Item "TilePlanner\bin\Release\net8.0-windows\TilePlanner.dll" -Destination "$subPath\TilePlanner.dll" -Force
```

---

## 🧪 V3.4 驗證點

### 1. 致命防呆測試
- **操作**: 在「寬度」輸入 `300mm` (含英文)，或留白。
- **點擊**: 按下「確定」。
- **預期**: 彈出「輸入格式錯誤」警告視窗，游標自動回到錯誤欄位，且彈窗不會關閉。

### 2. 鍵盤快速鍵測試
- **Enter**: 輸入完數值後直接按鍵盤 `Enter` -> 視窗應執行並關閉。
- **Esc**: 在任何時候按鍵盤 `Esc` -> 視窗應取消並關閉。

### 3. 動態介面功能
- **正排**: 交丁偏移面板完全隱藏。
- **交丁排**: 交丁偏移面板自動彈出，且「常用」比例按鈕功能正常。

---

## 📋 最終檢查表
- [x] TilePlannerDialog.cs：實作 TryParse 與分級驗證
- [x] UI 綁定：IsDefault/IsCancel 已確認
- [x] V3.4 部署與技術更新完成
