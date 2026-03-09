using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TilePlanner.Core;

namespace TilePlanner.Commands
{
    /// <summary>
    /// 切換磁磚格線整體連動功能
    /// 啟用後，移動一條格線時所有同方向格線會一起平移
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleLinkedMoveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                TileGridUpdater.IsEnabled = !TileGridUpdater.IsEnabled;

                string status = TileGridUpdater.IsEnabled ? "已啟用" : "已停用";
                TaskDialog.Show("磁磚計畫",
                    $"格線整體連動：{status}\n\n" +
                    (TileGridUpdater.IsEnabled
                        ? "移動任一格線時，同方向的所有格線將一起平移。"
                        : "格線將獨立移動。"));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"執行錯誤：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}
