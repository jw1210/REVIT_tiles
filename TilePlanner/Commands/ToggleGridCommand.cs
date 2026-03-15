using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TilePlanner.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleGridCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // ==========================================
            // [V3.6 核心修正] 移除 activeView.Id 的搜尋限制！
            // 改為從「整個專案 (doc)」去撈取，這樣即使被隱藏的線也能被找到
            // ==========================================
            var gridPlanes = new FilteredElementCollector(doc)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .Where(rp => rp.Name != null && rp.Name.Contains("TileGrid_"))
                .ToList();

            if (gridPlanes.Count == 0)
            {
                TaskDialog.Show("顯示/隱藏網格", "整個專案中找不到磁磚參考線。\n(請確認已建立磁磚計畫)");
                return Result.Succeeded;
            }

            using (Transaction trans = new Transaction(doc, "切換磁磚網格顯示"))
            {
                trans.Start();
                try
                {
                    // 分別篩選出在「當前視圖」中 可見(Visible) 與 隱藏(Hidden) 的參考線 ID
                    var visiblePlanes = gridPlanes.Where(rp => !rp.IsHidden(activeView)).Select(rp => rp.Id).ToList();
                    var hiddenPlanes = gridPlanes.Where(rp => rp.IsHidden(activeView)).Select(rp => rp.Id).ToList();

                    // 邏輯：只要畫面上有任何一條網格是顯示的，就把大家全部隱藏；
                    // 反之，如果畫面上全部的網格都已經隱藏了，就把它們全部叫出來。
                    if (visiblePlanes.Count > 0)
                    {
                        activeView.HideElements(visiblePlanes);
                    }
                    else if (hiddenPlanes.Count > 0)
                    {
                        // 取消隱藏時，必須確保傳入的 ID 確實處於隱藏狀態，否則 API 會報錯
                        activeView.UnhideElements(hiddenPlanes);
                    }

                    trans.Commit();
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    message = $"切換網格顯示失敗：{ex.Message}";
                    return Result.Failed;
                }
            }
        }
    }
}
