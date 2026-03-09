using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TilePlanner.Core;

namespace TilePlanner.Commands
{
    /// <summary>
    /// 移除磁磚計畫指令
    /// 支援帷幕牆、帷幕系統、帷幕屋頂
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveTilePlanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                Reference elemRef;
                try
                {
                    elemRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new CurtainElementSelectionFilter(),
                        "請選取要移除磁磚計畫的帷幕牆、帷幕系統或帷幕屋頂");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                Element selectedElement = doc.GetElement(elemRef.ElementId);
                List<CurtainGrid> grids = CurtainGridHelper.GetCurtainGrids(selectedElement);

                if (grids.Count == 0)
                {
                    TaskDialog.Show("磁磚計畫", "選取的元素沒有帷幕格線。");
                    return Result.Failed;
                }

                using (Transaction trans = new Transaction(doc, "移除磁磚計畫"))
                {
                    trans.Start();

                    try
                    {
                        foreach (CurtainGrid grid in grids)
                        {
                            RemoveGridContent(doc, grid);
                        }

                        // 刪除該元素上的設定資料，避免後續又被觸發
                        TileDataManager.RemoveTileConfig(selectedElement);

                        trans.Commit();
                        string typeName = CurtainGridHelper.GetElementTypeName(selectedElement);
                        TaskDialog.Show("磁磚計畫",
                            $"已移除{typeName}上的磁磚分割。");
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        message = $"移除失敗：{ex.Message}";
                        TaskDialog.Show("磁磚計畫 - 錯誤", message);
                        return Result.Failed;
                    }
                }
            }
            catch (Exception ex)
            {
                message = $"執行錯誤：{ex.Message}";
                return Result.Failed;
            }
        }

        private void RemoveGridContent(Document doc, CurtainGrid grid)
        {
            // 刪除竪框
            ICollection<ElementId> mullionIds = grid.GetMullionIds();
            foreach (ElementId id in mullionIds)
            {
                Mullion m = doc.GetElement(id) as Mullion;
                if (m != null && m.Pinned) m.Pinned = false;
            }
            if (mullionIds.Count > 0)
                doc.Delete(mullionIds);

            // 刪除水平格線
            foreach (ElementId id in grid.GetUGridLineIds())
            {
                try
                {
                    CurtainGridLine gl = doc.GetElement(id) as CurtainGridLine;
                    if (gl != null && gl.Pinned) gl.Pinned = false;
                    doc.Delete(id);
                }
                catch (Exception) { }
            }

            // 刪除垂直格線
            foreach (ElementId id in grid.GetVGridLineIds())
            {
                try
                {
                    CurtainGridLine gl = doc.GetElement(id) as CurtainGridLine;
                    if (gl != null && gl.Pinned) gl.Pinned = false;
                    doc.Delete(id);
                }
                catch (Exception) { }
            }
        }
    }
}
