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

                // 2. 尋找子品類：優先使用「磁磚切割網格」，若不存在則回退「磁磚計畫刀網」
                Category subCat = null;
                if (refPlaneCat.SubCategories.Contains("磁磚切割網格"))
                {
                    subCat = refPlaneCat.SubCategories.get_Item("磁磚切割網格");
                }
                else if (refPlaneCat.SubCategories.Contains("磁磚計畫刀網"))
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
                    
                    // 取得子品類當前的隱藏狀態
                    bool currentlyHidden = doc.ActiveView.GetCategoryHidden(subCat.Id);
                    
                    // 如果原本是隱藏的，現在使用者要「開啟」：
                    if (currentlyHidden)
                    {
                        // 1. 強制確保母品類「參照平面」是開啟的
                        if (doc.ActiveView.CanCategoryBeHidden(refPlaneCat.Id))
                        {
                            doc.ActiveView.SetCategoryHidden(refPlaneCat.Id, false);
                        }
                        // 2. 開啟子品類
                        if (doc.ActiveView.CanCategoryBeHidden(subCat.Id))
                        {
                            doc.ActiveView.SetCategoryHidden(subCat.Id, false);
                        }
                    }
                    else
                    {
                        // 如果原本是開啟的，直接關閉子品類即可
                        if (doc.ActiveView.CanCategoryBeHidden(subCat.Id))
                        {
                            doc.ActiveView.SetCategoryHidden(subCat.Id, true);
                        }
                    }

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
