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

            // [V3.5 修正] 直接抓取當前視圖中，所有檔名帶有 TileGrid_ 的參考平面
            var gridPlanes = new FilteredElementCollector(doc, activeView.Id)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .Where(rp => rp.Name != null && rp.Name.Contains("TileGrid_"))
                .ToList();

            if (gridPlanes.Count == 0)
            {
                TaskDialog.Show("顯示/隱藏網格", "當前視圖找不到磁磚參考線。\n(請確認已建立磁磚計畫，或是當前視圖無網格存在)");
                return Result.Succeeded;
            }

            using (Transaction trans = new Transaction(doc, "切換磁磚網格顯示"))
            {
                trans.Start();
                try
                {
                    // 檢查第一條參考線目前的狀態 (隱藏 或 顯示)
                    bool isHidden = gridPlanes.First().IsHidden(activeView);
                    List<ElementId> idsToToggle = gridPlanes.Select(rp => rp.Id).ToList();

                    // 強制針對這些實體執行隱藏/取消隱藏
                    if (isHidden)
                    {
                        activeView.UnhideElements(idsToToggle);
                    }
                    else
                    {
                        activeView.HideElements(idsToToggle);
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
