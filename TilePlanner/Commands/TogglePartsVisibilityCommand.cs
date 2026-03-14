using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TilePlanner.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TogglePartsVisibilityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // 防呆：檢查當前視圖是否支援零件可見性設定 (例如某些明細表或圖紙不支援)
            if (!activeView.CanEnableTemporaryViewPropertiesMode() && activeView.ViewType != ViewType.ThreeD && activeView.ViewType != ViewType.FloorPlan && activeView.ViewType != ViewType.Elevation && activeView.ViewType != ViewType.Section)
            {
                TaskDialog.Show("切換零件", "當前視圖不支援更改零件可見性。");
                return Result.Failed;
            }

            using (Transaction trans = new Transaction(doc, "切換零件/原主體顯示"))
            {
                trans.Start();

                try
                {
                    // 邏輯：如果目前是顯示原主體，就切換成顯示零件；否則切換回原主體
                    if (activeView.PartsVisibility == PartsVisibility.ShowOriginalOnly)
                    {
                        activeView.PartsVisibility = PartsVisibility.ShowPartsOnly;
                    }
                    else
                    {
                        activeView.PartsVisibility = PartsVisibility.ShowOriginalOnly;
                    }

                    trans.Commit();
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    message = $"切換視圖狀態失敗：{ex.Message}";
                    return Result.Failed;
                }
            }
        }
    }
}
