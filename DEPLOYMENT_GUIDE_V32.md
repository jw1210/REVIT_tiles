# TilePlanner V3.2 — 一鍵即時更新部署指南

## 📦 部署步驟

### Step 1: 關閉 Revit 2025
- **必備**: 確保 Revit 完全關閉以覆寫被鎖定的 `TilePlanner.dll`。

### Step 2: 編譯專案
```powershell
dotnet build TilePlanner.sln /p:Configuration=Release /p:Platform="Any CPU"
```

### Step 3: 手動佈署
```powershell
$addinsPath = "$($env:AppData)\Autodesk\Revit\Addins\2025"
$subPath = "$addinsPath\TilePlanner"
Copy-Item "TilePlanner\bin\Release\net8.0-windows\TilePlanner.dll" -Destination "$subPath\TilePlanner.dll" -Force
```

---

## 🧪 V3.2 驗證點

### 1. 基石測試：一鍵即時同步
- **操作**: 選取已排磚的牆，手動拉伸牆長（例如由 300cm 拉至 500cm）。
- **點擊**: 直接點擊「建立磁磚計畫」**一次**。
- **預期**: 舊磚塊瞬間消失，新磚塊**立刻鋪滿**至 500cm 處，無需再次點擊。

### 2. 靜默穩定性
- **預期**: 執行過程中依然保持完全靜默，無任何 HTML 彈窗。

---

## 📋 最終檢查表
- [x] 代碼邏輯：先 `Regenerate` 再 `GetTargetFace`
- [x] V3.2 技術指南已產出
- [x] 舊指南清理完成
