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
                Reference elemRef;
                try
                {
                    elemRef = uidoc.Selection.PickObject(ObjectType.Element, new PartSelectionFilter(), "請選取要進行磁磚分割的牆面、樓板或已建立的實體零件 (Part)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
                Element selectedElement = doc.GetElement(elemRef.ElementId);

                TilePlannerDialog dialog = new TilePlannerDialog();
                if (dialog.ShowDialog() != true)
                    return Result.Cancelled;

                TileConfig config = dialog.GetTileConfig();

                // ==========================================
                // [V3.1 架構] 單一 Master Transaction 
                // ==========================================
                using (Transaction masterTrans = new Transaction(doc, "AntiGravity 磁磚計畫 (V3.1)"))
                {
                    // 掛載全域警告吞噬者，保證 100% 靜默
                    FailureHandlingOptions options = masterTrans.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(new WarningSwallower());
                    masterTrans.SetFailureHandlingOptions(options);

                    masterTrans.Start();

                    try
                    {
                        // 1. 自動建立零件
                        if (selectedElement is Wall || selectedElement is Floor || selectedElement is RoofBase)
                        {
                            if (PartUtils.AreElementsValidForCreateParts(doc, new List<ElementId> { selectedElement.Id }))
                            {
                                PartUtils.CreateParts(doc, new List<ElementId> { selectedElement.Id });
                                doc.Regenerate(); 

                                ICollection<ElementId> parts = PartUtils.GetAssociatedParts(doc, selectedElement.Id, true, true);
                                if (parts.Count > 0)
                                    selectedElement = doc.GetElement(parts.First());
                                else
                                    throw new Exception("無法從選取的物件產生零件實體，請確認其可見性。");
                            }
                            else
                            {
                                if (PartUtils.HasAssociatedParts(doc, selectedElement.Id))
                                {
                                    ICollection<ElementId> parts = PartUtils.GetAssociatedParts(doc, selectedElement.Id, true, true);
                                    if (parts.Count > 0)
                                        selectedElement = doc.GetElement(parts.First());
                                }
                                else
                                {
                                    throw new Exception("選取的物件不支援建立零件 (Parts)。");
                                }
                            }
                        }

                        // 2. 執行核心分割引擎 (內部純邏輯，無 Transaction)
                        TilePartEngine engine = new TilePartEngine(doc, config);
                        engine.ExecuteOnElement(selectedElement);

                        // 3. 切換當前視圖顯示狀態
                        Parameter pv = doc.ActiveView.get_Parameter(BuiltInParameter.VIEW_PARTS_VISIBILITY);
                        if (pv != null && !pv.IsReadOnly)
                        {
                            pv.Set((int)PartsVisibility.ShowPartsOnly);
                        }

                        // 4. 一次性提交所有變更，消滅所有HTML錯誤報告
                        masterTrans.Commit();

                        TaskDialog.Show("磁磚計畫 - 完成", "實體磁磚零件 (Parts) 已生成並切割完成！\n網格已鎖定，可隨意拖拉連動。");
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

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
