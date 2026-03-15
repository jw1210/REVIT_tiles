using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TilePlanner.Core;
using TilePlanner.UI;

namespace TilePlanner.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTilePlanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                IList<Reference> elemRefs;
                try
                {
                    // [V3.7 升級] 改為 PickObjects，允許按住 Ctrl 或拉框選取多道牆面/樓板
                    elemRefs = uidoc.Selection.PickObjects(ObjectType.Element, new PartSelectionFilter(), "請選取「一或多個」要進行磁磚分割的牆面、樓板或實體零件 (Part)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (elemRefs == null || elemRefs.Count == 0) return Result.Cancelled;

                TilePlannerDialog dialog = new TilePlannerDialog();
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                TileConfig config = dialog.GetTileConfig();

                using (Transaction masterTrans = new Transaction(doc, "AntiGravity 磁磚計畫 V3.7 (批次分割)"))
                {
                    FailureHandlingOptions options = masterTrans.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(new WarningSwallower());
                    masterTrans.SetFailureHandlingOptions(options);

                    masterTrans.Start();

                    try
                    {
                        List<ElementId> elementsToCreateParts = new List<ElementId>();
                        List<Element> finalElementsToProcess = new List<Element>();

                        // 1. 分類選取的物件，提取需要建立 Part 的母體
                        foreach (Reference r in elemRefs)
                        {
                            Element el = doc.GetElement(r);
                            if (el is Wall || el is Floor || el is RoofBase || el is Ceiling)
                            {
                                if (PartUtils.AreElementsValidForCreateParts(doc, new List<ElementId> { el.Id }))
                                {
                                    elementsToCreateParts.Add(el.Id);
                                }
                                else if (PartUtils.HasAssociatedParts(doc, el.Id))
                                {
                                    var parts = PartUtils.GetAssociatedParts(doc, el.Id, true, true);
                                    if (parts.Count > 0) finalElementsToProcess.Add(doc.GetElement(parts.First()));
                                }
                            }
                            else if (el is Part)
                            {
                                finalElementsToProcess.Add(el);
                            }
                        }

                        // 2. 批次建立 Parts (一次性生成效能最高)
                        if (elementsToCreateParts.Count > 0)
                        {
                            PartUtils.CreateParts(doc, elementsToCreateParts);
                            doc.Regenerate(); 

                            foreach (ElementId id in elementsToCreateParts)
                            {
                                var parts = PartUtils.GetAssociatedParts(doc, id, true, true);
                                if (parts.Count > 0) finalElementsToProcess.Add(doc.GetElement(parts.First()));
                            }
                        }

                        // 3. 批次執行核心分割引擎
                        TilePartEngine engine = new TilePartEngine(doc, config);
                        int successCount = 0;

                        foreach (Element targetEl in finalElementsToProcess)
                        {
                            try
                            {
                                engine.ExecuteOnElement(targetEl);
                                successCount++;
                            }
                            catch { /* 靜默處理單一失敗，確保其他牆面能繼續完成 */ }
                        }

                        // 4. 切換當前視圖顯示狀態為零件
                        Parameter pv = doc.ActiveView.get_Parameter(BuiltInParameter.VIEW_PARTS_VISIBILITY);
                        if (pv != null && !pv.IsReadOnly)
                        {
                            pv.Set((int)PartsVisibility.ShowPartsOnly);
                        }

                        masterTrans.Commit();

                        TaskDialog.Show("磁磚計畫 - 批次完成", $"✅ 成功處理了 {successCount} 個面的磁磚分割！\n網格已鎖定，可隨意拖拉連動。");
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        masterTrans.RollBack();
                        message = $"磁磚切割失敗：{ex.Message}";
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
    }

    public class PartSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Part || elem is Wall || elem is Floor || elem is RoofBase || elem is Ceiling;
        }
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
