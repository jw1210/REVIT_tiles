# TilePlanner V3.1 — 單一主交易與獨立按鈕部署指南

## 📦 部署步驟

### Step 1: 關閉 Revit
- **重要**: 請先關閉 Revit 2025 以解除 DLL 鎖定。

### Step 2: 編譯專案
```powershell
dotnet build TilePlanner.sln /p:Configuration=Release /p:Platform="Any CPU"
```

### Step 3: 手動佈署 (或使用腳本)
```powershell
$addinsPath = "$($env:AppData)\Autodesk\Revit\Addins\2025"
Copy-Item "TilePlanner\bin\Release\net8.0-windows\TilePlanner.dll" -Destination $addinsPath -Force
Copy-Item "TilePlanner.addin" -Destination $addinsPath -Force
```

---

## 🧪 V3.1 驗證點

### 1. 終極靜默測試
- **操作**: 執行「建立磁磚計畫」。
- **預期**: 整個生成過程完全安靜，Transaction 提交後**絕對不會**彈出 HTML 警告報告。

### 2. UI 版面驗證
- **預期**: 面板應顯示 5 個獨立的大按鈕。
- **按鈕標題**: 應正確換行（如：顯示/隱藏\n零件）。

### 3. 全域 3D 切換
- **操作**: 在 3D 視圖點擊「顯示/隱藏 零件」。
- **預期**: 正常切換，無報錯。

---

## 📋 最終檢查表
- [x] 代碼無 `TransactionGroup`（已移除）
- [x] 所有警告由 `WarningSwallower` 處理
- [x] dll 已成功覆寫 (Revit 關閉狀態)
- [x] V3.1 技術指南已產出
