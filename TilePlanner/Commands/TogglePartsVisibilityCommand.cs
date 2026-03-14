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

            // [V3.1] 精準抓取底層參數，完美支援 3D 視角與各類平面圖
            Parameter pv = activeView.get_Parameter(BuiltInParameter.VIEW_PARTS_VISIBILITY);
            
            if (pv == null)
            {
                TaskDialog.Show("切換零件", "當前視圖類型 (如明細表) 不支援零件顯示設定。");
                return Result.Failed;
            }

            if (pv.IsReadOnly)
            {
                TaskDialog.Show("切換零件", "當前視圖的零件顯示受到「視圖樣板 (View Template)」控制，請先解除樣板鎖定。");
                return Result.Failed;
            }

            using (Transaction trans = new Transaction(doc, "切換零件/原主體顯示"))
            {
                trans.Start();
                try
                {
                    // 若目前是「僅顯示原主體」，則切換為「僅顯示零件」；反之亦然
                    if (pv.AsInteger() == (int)PartsVisibility.ShowOriginalOnly)
                    {
                        pv.Set((int)PartsVisibility.ShowPartsOnly);
                    }
                    else
                    {
                        pv.Set((int)PartsVisibility.ShowOriginalOnly);
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
