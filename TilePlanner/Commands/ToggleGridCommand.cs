using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TilePlanner.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ToggleGridCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;

            try
            {
                // 1. 取得主品類：參照平面 (OST_CLines)
                Category refPlaneCat = Category.GetCategory(doc, BuiltInCategory.OST_CLines);

                if (refPlaneCat == null)
                {
                    TaskDialog.Show("TilePlanner", "無法取得參照平面品類。");
                    return Result.Failed;
                }

                // 2. 尋找子品類：磁磚計畫刀網 (V2.X 名稱)
                Category subCat = null;
                if (refPlaneCat.SubCategories.Contains("磁磚計畫刀網"))
                {
                    subCat = refPlaneCat.SubCategories.get_Item("磁磚計畫刀網");
                }

                if (subCat == null)
                {
                    TaskDialog.Show("TilePlanner", "目前專案中未找到「磁磚計畫刀網」子品類。請先執行一次磁磚計畫。");
                    return Result.Failed;
                }

                // 3. 切換可見性 (Toggle Visibility)
                using (Transaction t = new Transaction(doc, "切換磁磚網格顯示"))
                {
                    t.Start();
                    
                    bool isHidden = subCat.get_Visible(doc.ActiveView) == false; // get_Visible 為 true 時代表顯示中
                    
                    // 如果原本被隱藏，則顯示；反之則隱藏
                    doc.ActiveView.SetCategoryHidden(subCat.Id, !isHidden);

                    t.Commit();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
