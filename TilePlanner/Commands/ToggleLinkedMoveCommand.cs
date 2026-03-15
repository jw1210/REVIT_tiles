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
            // [V3.9.3] 授權驗證
            if (!TilePlanner.Security.LicenseManager.Validate()) return Result.Failed;

            try
            {
                TaskDialog.Show("磁磚計畫 - 網格連動",
                    "零件分割(Divide Parts) 模式下，已不再使用格線連動功能。\n" +
                    "如需修改分割，請重新執行「建立磁磚計畫」指令以覆蓋。");

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
