using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TilePlanner.Core;
using TilePlanner.Core.Services;

namespace TilePlanner.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveTilePlanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // [V3.9.3] 授權驗證
            if (!TilePlanner.Security.LicenseManager.Validate()) return Result.Failed;

            try
            {
                Reference elemRef = uidoc.Selection.PickObject(ObjectType.Element, new PartSelectionFilter(), "請選取要移除磁磚計畫的實體零件 (Part)");
                Element selectedElement = doc.GetElement(elemRef.ElementId);
                if (!(selectedElement is Part part)) return Result.Failed;

                // [V2.5 核心修正] 向上遞迴追溯真正的宿主
                ElementId currentId = part.Id;
                Element currentElement = part;
                while (currentElement is Part p)
                {
                    var sourceIds = p.GetSourceElementIds();
                    if (sourceIds == null || sourceIds.Count == 0) break;
                    currentId = sourceIds.First().HostElementId;
                    currentElement = doc.GetElement(currentId);
                }
                ElementId trueHostId = currentId;

                using (Transaction trans = new Transaction(doc, "移除磁磚計畫"))
                {
                    trans.Start();
                    try
                    {
                        var rootParts = PartUtils.GetAssociatedParts(doc, trueHostId, false, false);
                        foreach (var rootPartId in rootParts)
                        {
                            var pm = PartUtils.GetAssociatedPartMaker(doc, rootPartId);
                            if (pm != null && doc.GetElement(pm.Id) != null) doc.Delete(pm.Id);
                        }

                        RevitGridService gridService = new RevitGridService(doc);
                        gridService.ClearOldGridElements(trueHostId);
                        TileDataManager.RemoveTileConfig(currentElement);

                        trans.Commit();
                        TaskDialog.Show("磁磚計畫", "已成功移除磁磚分割，整個牆面已還原為原本狀態。");
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        message = $"移除失敗：{ex.Message}";
                        return Result.Failed;
                    }
                }
            }
            catch { return Result.Failed; }
        }
    }
}
