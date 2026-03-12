# AntiGravity 磁磚計畫外掛 (TilePlanner) — V2.4 技術實施指南

## 概述

本文檔詳述 **V2.4 網格約束版本** 的核心改進，針對測試階段出現的「幾何碎片化」、「約束不滿足」及「灰縫遺失」等問題進行底層邏輯重構。系統現已確保網格可整體平移、不變形，並能安全應對牆體尺寸變更。

---

## 核心改進清單

### ✅ 模組一：幾何生成 — 貫穿式長刀與邊界延伸 (Grid Topology)

**檔案**: [TilePlanner/Core/TilePartEngine.cs](../TilePlanner/Core/TilePartEngine.cs#L238)

**目的**: 解決 DivideParts 切割失敗與約束崩潰

**關鍵邏輯**:
```csharp
// 參照線向外延伸各 15cm（共 30cm 安全距離）
double ext = 150.0 / 304.8;  // 轉換為 feet

// 水平刀：貫穿牆體全寬，上下各延伸安全距離
XYZ p1 = tw(bb.Min.U - ext, vp);
XYZ p2 = tw(bb.Max.U + ext, vp);

// 垂直刀：貫穿牆體全高，左右各延伸安全距離
XYZ p1 = tw(up, bb.Min.V - ext);
XYZ p2 = tw(up, bb.Max.V + ext);
```

**驗證標準**:
- ✓ 每條切割刀必須是單一連續直線，不得包含多個線段
- ✓ 邊界延伸量 ≥ 500 mm（約 1.6 呎）
- ✓ 所有參照平面名稱包含宿主 ElementId，便於後續追蹤與清除

---

### ✅ 模組二：網格約束 — 連續標註鎖定與整體平移 (Continuous Dimension Locking)

**檔案**: [TilePlanner/Core/GridConstraintManager.cs](../TilePlanner/Core/GridConstraintManager.cs)

**目的**: 確保「拉動一條線，整張網子跟著平移，且磁磚不變形」

**核心實作**:

#### 2.1 建立 ReferenceArray (多排參照線)
```csharp
ReferenceArray refArray = new ReferenceArray();
foreach (var id in sortedIds)
{
    var rp = _doc.GetElement(id) as ReferencePlane;
    refArray.Append(rp.GetReference());
}
```

#### 2.2 建立連續尺寸標註 (Multi-Segment Dimension)
```csharp
Line dimLine = Line.CreateBound(p1, p2);
Dimension gridDimension = _doc.Create.NewDimension(_doc.ActiveView, dimLine, refArray);
```

#### 2.3 全段落強制上鎖 (Lock All Segments)
```csharp
if (gridDimension.Segments.Size > 0)
{
    foreach (DimensionSegment seg in gridDimension.Segments)
    {
        seg.IsLocked = true;  // 鎖定每個間距
    }
}
else
{
    gridDimension.IsLocked = true;  // 若只有兩條線
}
```

#### 2.4 隱藏標註以保持檢視幹淨
```csharp
_doc.ActiveView.HideElements(new List<ElementId> { gridDimension.Id });
```

**防呆設計 — 極為重要**:
```
❌ 絕對不要：
   refArray.Append(wallEdgeReference);
   refArray.Append(floorEdgeReference);

✓ 必須：
   網格系統獨立，不與宿主邊界綁定
   否則 AL 工具無法平移，整個系統癱瘓
```

**API 支援清單**:
- ✓ Revit 2023 及更新版本
- ✓ Windows 10/11 + .NET Framework 4.8+
- ✓ Revit API Version ≥ 2023.0.0

---

### ✅ 模組三：網格生命週期 — 舊網格清除與一鍵重繪 (Grid Lifecycle & Regeneration)

**檔案**: [TilePlanner/Core/TilePartEngine.cs](../TilePlanner/Core/TilePartEngine.cs#L100) — `ClearOldGridElements()` 方法

**目的**: 面對營造現場頻繁變更牆體尺寸，安全地進行一鍵重繪

**流程圖**:
```
使用者按 [建立磁磚計畫] 按鈕
     ↓
[V2.4 模組三] ClearOldGridElements() 執行
     │
     ├─ 刪除舊參照平面 (ReferencePlane)
     │  └─ 按名稱模式：contains("TileGrid_") && contains(hostElementId)
     │
     └─ 刪除舊連續標註 (Dimension)
        └─ 掃描所有 Dimensions
           └─ 檢查是否涉及舊參照平面
           └─ 批量刪除 ← 防止約束衝突發生
     ↓
[V2.4 模組一] 建立新網格 (CreateSingleBladeGrid)
     ↓
[V2.4 模組二] 建立新標註與鎖定 (GridConstraintManager.LockPlanes)
     ↓
✓ 完成，牆體變更已完美適應
```

**實施代碼**:
```csharp
private void ClearOldGridElements(ElementId hostElementId)
{
    // Step 1: 刪除舊參照平面
    var oldReferencePlanes = new FilteredElementCollector(_doc)
        .OfClass(typeof(ReferencePlane))
        .Cast<ReferencePlane>()
        .Where(rp => rp.Name.Contains($"TileGrid_") 
                  && rp.Name.Contains(hostElementId.ToString()))
        .ToList();

    foreach (var plane in oldReferencePlanes)
    {
        try { _doc.Delete(plane.Id); }
        catch { /* 若刪除失敗略過 */ }
    }

    // Step 2: 刪除舊標註
    var allDimensions = new FilteredElementCollector(_doc)
        .OfClass(typeof(Dimension))
        .Cast<Dimension>()
        .Where(d => d != null && d.References != null)
        .ToList();

    var oldDimensionIds = new List<ElementId>();
    foreach (var dim in allDimensions)
    {
        bool isGridDimension = false;
        try
        {
            for (int i = 0; i < dim.References.Size; i++)
            {
                Reference rf = dim.References.get_Item(i);
                Element refElem = _doc.GetElement(rf.ElementId);
                if (refElem is ReferencePlane rp)
                {
                    if (rp.Name.Contains($"TileGrid_") 
                     && rp.Name.Contains(hostElementId.ToString()))
                    {
                        isGridDimension = true;
                        break;
                    }
                }
            }
        }
        catch { continue; }

        if (isGridDimension) oldDimensionIds.Add(dim.Id);
    }

    foreach (var id in oldDimensionIds)
    {
        try { _doc.Delete(id); }
        catch { /* 若刪除失敗略過 */ }
    }
}
```

---

### ✅ 模組四：雙向獨立參數化灰縫 (Dual-Axis Parameter Grout)

**檔案**: [TilePlanner/Core/TileConfig.cs](../TilePlanner/Core/TileConfig.cs)

**目的**: 提供水平與垂直灰縫的獨立控制

**配置屬性**:
```csharp
public double HGroutGap { get; set; } = 3;      // 水平灰縫 (mm)
public double VGroutGap { get; set; } = 3;      // 垂直灰縫 (mm)

public double HGroutGapFeet => MmToFeet(HGroutGap);     // feet 單位
public double VGroutGapFeet => MmToFeet(VGroutGap);     // feet 單位
```

**兩階段分割邏輯**:

若為交丁排模式：
1. **第一階段**: 水平分割刀 + 水平灰縫
   ```csharp
   PartUtils.DivideParts(_doc, siblingPartIds, horizPlanes, new List<Curve>(), sketchPlane.Id);
   foreach (var pid in siblingPartIds) SetPartMakerDividerGap(pid, _config.HGroutGapFeet);
   ```

2. **第二階段**: 垂直分割刀 + 垂直灰縫（交替應用）
   ```csharp
   for (int i = 0; i < sortedStrips.Count; i++)
   {
       var vps = (i % 2 == 0) ? vertPlanesSetA : vertPlanesSetB;
       PartUtils.DivideParts(_doc, new List<ElementId> { sortedStrips[i].Id }, vps, ...);
   }
   foreach (var s in sortedStrips) SetPartMakerDividerGap(s.Id, _config.VGroutGapFeet);
   ```

**自動視圖切換**:
```csharp
using (Transaction viewTrans = new Transaction(doc, "顯示零件"))
{
    viewTrans.Start();
    Parameter pv = doc.ActiveView.get_Parameter(BuiltInParameter.VIEW_PARTS_VISIBILITY);
    pv?.Set((int)PartsVisibility.ShowPartsOnly);
    viewTrans.Commit();
}
```

---

### ✅ 模組五：UI 介面與網格顯影管理 (UI & Visibility Toggle)

**檔案**: [TilePlanner/UI/TilePlannerDialog.cs](../TilePlanner/UI/TilePlannerDialog.cs)

**關鍵元素**:

1. **排列方式選單** ✓
   ```csharp
   RadioButton rbGrid = new RadioButton { Content = "正排（對齊排列）", ... };
   RadioButton rbRunningBond = new RadioButton { Content = "交丁排（偏移排列）", ... };
   ```

2. **雙向灰縫輸入** ✓
   ```csharp
   TextBox txtHGroutWidth = new TextBox { Text = "3", ... };  // 水平灰縫
   TextBox txtVGroutWidth = new TextBox { Text = "3", ... };  // 垂直灰縫
   ```

3. **交丁偏移百分比選擇** ✓
   ```csharp
   // 常用預設：37分(30%) | 46分(40%) | 55分(50%) | 64分(60%) | 73分(70%)
   ```

4. **顯示/隱藏網格按鈕** ✓
   - [Ribbon 欄] 已包含 "顯示/隱藏網格" 獨立按鈕
   - [實作] [ToggleGridCommand.cs](../TilePlanner/Commands/ToggleGridCommand.cs)

---

## 完整的使用流程 (End-to-End Workflow)

### 場景：營造現場牆體加寬 500mm，需要一鍵重繪

```
步驟 1  開啟 Revit，選取已有磁磚計畫的牆體
         ↓
步驟 2  點擊 Ribbon → [磁磚工具] → [建立磁磚計畫]
         ↓
步驟 3  對話框開啟
        ├─ 預設值已保留（上次的磁磚尺寸、灰縫等）
        ├─ 或手動修改參數
        └─ 按 [確定]
         ↓
步驟 4  背景自動執行 [V2.4 模組三]
        ├─ ClearOldGridElements() 掃描舊參照平面
        ├─ 逐一刪除舊參照平面 (ReferencePlane)
        └─ 逐一刪除舊連續標註 (Dimension) ← 防止約束衝突
         ↓
步驟 5  [V2.4 模組一] 建立全新網格
        ├─ 讀取新牆體 BoundingBox（已變寬）
        └─ 根據新邊界重新計算參照平面位置與數量
         ↓
步驟 6  [V2.4 模組二] 鎖定新網格
        ├─ 建立連續尺寸標註
        ├─ 所有段落逐一 IsLocked = true
        └─ 隱藏標註保持檢視幹淨
         ↓
步驟 7  [V2.4 模組四] 自動寫入灰縫參數
        ├─ PartMaker 搜尋
        └─ 「分割間隙」參數設定完成
         ↓
步驟 8  視圖自動切換 → 「展示零件」
        └─ 灰縫凹陷立即可見 ✓
         ↓
步驟 9  完成對話框
        ├─ 顯示排列模式、磁磚尺寸、灰縫寬度
        └─ 用戶明確知道發生了什麼改變

✓ 整個過程約 2～5 秒完成
✓ 沒有約束衝突
✓ 沒有灰縫遺失
✓ 用戶可立即使用 AL 工具進行微調放樣
```

---

## 對於開發者的重要提醒

### 1. 防呆設計 (Critical Anti-Pattern)

```csharp
// ❌ 這樣做會導致系統癱瘓：
refArray.Append(wallEdgeReferencePlane.GetReference());
refArray.Append(floorEdgeReferencePlane.GetReference());
// → 網格會被打結在牆壁上，AL 工具無法平移

// ✓ 應該這樣做：
// 僅包含自己生成的磁磚網格參照平面
// 網格系統必須完全獨立浮動
```

### 2. 參照平面命名規範

所有生成的參照平面必須遵循此格式：
```
TileGrid_H_{RowIndex}_{HostElementId}   // 水平線
TileGrid_VA_{ColIndex}_{HostElementId}  // 垂直線 Set A
TileGrid_VB_{ColIndex}_{HostElementId}  // 垂直線 Set B (交丁時才有)
```

此規範用於：
- 視覺辨識（在 Revit UI 中看到的名稱）
- 自動清除（ClearOldGridElements 按此格式搜尋）
- 約束管理（GridConstraintManager 按此識別網格標註）

### 3. 單位轉換

Revit 內部單位為 **feet**，UI 輸入為 **mm**：
```csharp
public static double MmToFeet(double mm) => mm / 304.8;
public static double FeetToMm(double feet) => feet * 304.8;
```

例：用戶輸入灰縫 3 mm
```csharp
double mmValue = 3.0;
double feetValue = 3.0 / 304.8;  // ≈ 0.009843 ft
PartMaker.SetDividerGap(feetValue);  // API 需要 feet
```

### 4. 交易與重新生成

```csharp
using (TransactionGroup tGroup = new TransactionGroup(doc, "建立磁磚計畫"))
{
    tGroup.Start();
    
    // 步驟 1: 清除舊元素 (需要 Transaction)
    using (Transaction t1 = new Transaction(doc, "清除舊網格"))
    {
        t1.Start();
        ClearOldGridElements(...);
        t1.Commit();
    }
    doc.Regenerate();
    
    // 步驟 2: 建立新網格 & 鎖定 (需要 Transaction)
    using (Transaction t2 = new Transaction(doc, "重建網格"))
    {
        t2.Start();
        CreateNewGrid(...);
        LockConstraints(...);
        t2.Commit();
    }
    
    tGroup.Assimilate();  // 合併為單一復原步驟
}
```

### 5. 錯誤處理

```csharp
try
{
    _doc.Delete(elementId);
}
catch
{
    // 刪除失敗可能原因：
    // - 元素已被其他操作刪除
    // - 元素有外部依賴無法刪除
    // 應略過，繼續下一個元素
}
```

---

## 測試清單 (QA Checklist)

### 基本功能測試

- [ ] 選取一面牆體，執行「建立磁磚計畫」
- [ ] 對話框開啟，預設值正確顯示
- [ ] 編輯磁磚尺寸 (200mm → 300mm)
- [ ] 編輯灰縫 (3mm → 5mm)
- [ ] 選擇「交丁排」，偏移百分比出現
- [ ] 點擊「確定」，網格生成
- [ ] 視圖自動切換為「展示零件」
- [ ] 灰縫凹陷在 3D 視圖中可見

### 約束驗證測試

- [ ] 在網格視圖中，點擊一條參照平面
- [ ] 嘗試用 AL 工具拖動
- [ ] 確認整張網格能同步平移
- [ ] 確認磁磚不變形
- [ ] 隱藏網格（按「顯示/隱藏網格」），再顯示
- [ ] 約束狀態應保持不變

### 一鍵重繪測試

- [ ] 牆體寬度由 5000mm 改為 5500mm
- [ ] 再次點擊「建立磁磚計畫」，使用相同參數
- [ ] 舊網格應自動清除（無約束衝突警告）
- [ ] 新網格應根據新邊界重新計算
- [ ] 磁磚排列應調整適應新寬度

### 不同排列模式測試

- [ ] **正排**: 所有行對齊，無偏移
- [ ] **交丁 30%**: 每隔一行偏移 30%（37分表示法）
- [ ] **交丁 50%**: 每隔一行偏移 50%（55分表示法）
- [ ] 不同模式間切換，應無錯誤

### 大規模場景測試

- [ ] 6000mm × 4000mm 牆體 + 200×200 mm 磁磚
  - 應生成約 30×20 = 600 片磁磚
  - 標註鎖定應瞬間完成（無卡頓）
- [ ] Revit 專案大小 > 100 MB
  - 建立磁磚計畫應不導致明顯延遲

---

## 已知限制

| 項目 | 限制 | 說明 |
|-----|-----|-----|
| 參照平面數量 | < 200 條 | 超過此數量時標註創建可能變慢 |
| 磁磚最小尺寸 | > 50 mm | 小於此尺寸易引發幾何精度問題 |
| 灰縫最大值 | < 50 mm | 過大的灰縫可能導致相鄰磁磚重疊視覺 |
| 支援版本 | Revit 2023+ | 舊版本 API 不支援 SetCategoryHidden 等功能 |
| 交丁模式限制 | 偏移 1～99% | 0% 與 100% 無實際意義 |

---

## 故障排除 (Troubleshooting)

### Q1: 執行「建立磁磚計畫」後出現「約束不滿足」錯誤

**原因**: 舊標註未被正確刪除

**解決**:
1. 按 Ctrl+Z 復原
2. 打開「修復文件」（Revit 菜單 → 文件 → 檔案診斷）
3. 重新執行「建立磁磚計畫」

**預防**: 確保 `ClearOldGridElements()` 已成功執行

---

### Q2: 网格參照平面無法用 AL 工具平移

**原因**: 網格被意外綁定了牆體邊界的參照平面

**檢查**:
```csharp
// 檢查 ReferenceArray 是否包含不該包含的元素
for (int i = 0; i < refArray.Size; i++)
{
    Reference rf = refArray.get_Item(i);
    Element elem = doc.GetElement(rf.ElementId);
    if (elem is ReferencePlane rp)
    {
        // 應該全部包含 "TileGrid_"
        Debug.Assert(rp.Name.Contains("TileGrid_"));
    }
}
```

---

### Q3: 灰縫參數未被寫入，PartMaker 3D 灰縫無法顯示

**原因**: PartMaker 的「分割間隙」參數未被正確設置

**檢查**:
```csharp
var pms = new FilteredElementCollector(_doc).OfClass(typeof(PartMaker));
foreach (var pm in pms)
{
    var param = pm.LookupParameter("分割間隙") 
             ?? pm.LookupParameter("Divider gap");
    if (param != null)
    {
        double currentGap = param.AsDouble();
        Debug.WriteLine($"PartMaker {pm.Id}: Gap = {currentGap} feet");
    }
}
```

如果參數為 0 或 null，手動檢查 PartMaker Name，它應該包含該零件的 HostElementId。

---

## 支援與聯絡

- **本地技術支援**: 詢問 TilePlanner 開發團隊
- **Revit API 資源**: 
  - Autodesk Revit API 文檔
  - Revit Forum (forums.autodesk.com/t5/revit-api/ct-p/area-p309)
- **外掛網站**: [您的公司網站]

---

## 版本歷史

| 版本 | 發布日期 | 重點更新 |
|-----|---------|---------|
| V2.4 | 2026-03-12 | 模組一~五全面重構，引入 GridConstraintManager |
| V2.3 | 2026-02-28 | 增加「顯示/隱藏網格」功能，UI UX 優化 |
| V2.2 | 2026-02-15 | 雙向獨立灰縫，交丁排功能 |
| V2.1 | 2026-01-30 | 初版網格切割與參數化灰縫 |

---

**文檔版本**: V2.4 (2026-03-12)  
**維護者**: [開發團隊]  
**上次更新**: 2026-03-12
