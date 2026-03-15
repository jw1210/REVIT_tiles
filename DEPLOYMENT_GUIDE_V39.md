# TilePlanner V3.9 — 自訂轉角間距部署指南

## 📦 部署步驟

### Step 1: 關閉 Revit 2025
- **重要**: 確保 Revit 完全關閉，以釋放 DLL 檔案鎖定。

### Step 2: 編譯專案 (V3.9)
```powershell
dotnet build TilePlanner.sln /p:Configuration=Release /p:Platform="Any CPU"
```

### Step 3: 更新 DLL 與 Addin 檔案
進入 PowerShell 並執行以下指令：
```powershell
$addinsPath = "$($env:AppData)\Autodesk\Revit\Addins\2025"
$subPath = "$addinsPath\TilePlanner"

# 建立子目錄
New-Item -ItemType Directory -Force -Path $subPath

# 複製 Addin 清單
Copy-Item "TilePlanner.addin" -Destination "$addinsPath\TilePlanner.addin" -Force

# 複製核心 DLL
Copy-Item "TilePlanner\bin\Release\net8.0-windows\TilePlanner.dll" -Destination "$subPath\TilePlanner.dll" -Force
```

---

## 🧪 V3.9 驗證點

### 1. 動態彈窗測試
- **操作**: 點擊「局部轉角」按鈕，選擇任一形式。
- **預期**: 立刻彈出「設定轉角灰縫」視窗，預設值為 `1`。

### 2. 間距精準度測試
- **Miter (45度)**:
    - 輸入 `2.0`。
    - 觀察切割後轉角，兩側應平均退縮形成 2mm 的細縫。
- **Cover (蓋磚)**:
    - 輸入 `5.0`。
    - 觀察被蓋側磁磚，應與蓋人側內緣保持 5mm 距離。

---

## 📋 最終檢查表
- [x] ManualCornerJoinCommand.cs：已修正 TransactionGroup 報錯並實作動態 WPF。
- [x] .addin 檔案：已指向最新的 V3.9 DLL 路徑。
- [x] V3.9 部署完成。
