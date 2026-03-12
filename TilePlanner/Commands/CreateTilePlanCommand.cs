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
    /// <summary>
    /// 建立磁磚計畫指令
    /// 支援：帷幕牆 (Wall)、帷幕系統 (CurtainSystem)、
    ///       帷幕屋頂 (FootPrintRoof / ExtrusionRoof)
    /// </summary>
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
                    elemRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new PartSelectionFilter(),
                        "請選取要進行磁磚分割的牆面、樓板或已建立的實體零件 (Part)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
                Element selectedElement = doc.GetElement(elemRef.ElementId);

                // 開啟參數設定對話框
                TilePlannerDialog dialog = new TilePlannerDialog();
                if (dialog.ShowDialog() != true)
                    return Result.Cancelled;

                TileConfig config = dialog.GetTileConfig();

                using (TransactionGroup tGroup = new TransactionGroup(doc, "建立磁磚計畫 (Two-Stage)"))
                {
                    tGroup.Start();

                    try
                    {
                        // 檢查並自動建立零件 (Auto-Create Part)
                        if (selectedElement is Wall || selectedElement is Floor || selectedElement is RoofBase)
                        {
                            if (PartUtils.AreElementsValidForCreateParts(doc, new List<ElementId> { selectedElement.Id }))
                            {
                                // 將目標轉為零件 (需要包在 Transaction 內)
                                using (Transaction trans = new Transaction(doc, "建立初階零件"))
                                {
                                    trans.Start();
                                    PartUtils.CreateParts(doc, new List<ElementId> { selectedElement.Id });
                                    doc.Regenerate(); // 重新產生以獲取剛建立的零件
                                    trans.Commit();
                                }

                                // 找到這個牆/樓板剛剛生成的所有零件
                                ICollection<ElementId> parts = PartUtils.GetAssociatedParts(doc, selectedElement.Id, true, true);
                                if (parts.Count > 0)
                                {
                                    // 預設拿第一個零件當作主要的面上瓷磚 (可能需要更聰明的篩選，這裡先取第一個)
                                    selectedElement = doc.GetElement(parts.First());
                                }
                                else
                                {
                                    throw new Exception("無法從選取的物件產生零件實體，請確認其可見性。");
                                }
                            }
                            else
                            {
                                // 確認它是否已經有零件了
                                if (PartUtils.HasAssociatedParts(doc, selectedElement.Id))
                                {
                                    ICollection<ElementId> parts = PartUtils.GetAssociatedParts(doc, selectedElement.Id, true, true);
                                    if (parts.Count > 0)
                                    {
                                        selectedElement = doc.GetElement(parts.First());
                                    }
                                }
                                else
                                {
                                    throw new Exception("選取的物件不支援建立零件 (Parts)。");
                                }
                            }
                        }

                        // 直接對選取的目標實體牆面/樓板進行 Parts 分割
                        TilePartEngine engine = new TilePartEngine(doc, config);
                        engine.ExecuteOnElement(selectedElement);

                        // 自動切換當前視圖為「展示零件 (Show Parts)」，確保灰縫立刻可見
                        using (Transaction viewTrans = new Transaction(doc, "TilePlanner - 顯示零件"))
                        {
                            viewTrans.Start();
                            Parameter pv = doc.ActiveView.get_Parameter(BuiltInParameter.VIEW_PARTS_VISIBILITY);
                            if (pv != null && !pv.IsReadOnly)
                            {
                                // 對應 UI 的「展示零件 (Show Parts)」
                                pv.Set((int)PartsVisibility.ShowPartsOnly);
                            }
                            viewTrans.Commit();
                        }

                        tGroup.Assimilate(); // 合併所有的子交易為一個復原動作

                        TaskDialog.Show("磁磚計畫",
                            $"實體磁磚零件 (Parts) 產生並切割完成！\n\n" +
                            $"當前視圖已自動切換為「展示零件 (Show Parts)」，若需檢視原始牆體，可在視圖屬性中調整「零件可見性」。\n");

                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        tGroup.RollBack();
                        message = $"磁磚切割失敗：{ex.Message}\n\n{ex.StackTrace}";
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

    /// <summary>
    /// 帷幕元素的通用輔助工具
    /// </summary>
    public static class CurtainGridHelper
    {
        /// <summary>
        /// 從元素取得所有 CurtainGrid（支援帷幕牆、帷幕系統、帷幕屋頂）
        /// </summary>
        public static List<CurtainGrid> GetCurtainGrids(Element element)
        {
            var result = new List<CurtainGrid>();

            // 帷幕牆
            if (element is Wall wall && wall.CurtainGrid != null)
            {
                result.Add(wall.CurtainGrid);
                return result;
            }

            // 帷幕系統
            if (element is CurtainSystem cs && cs.CurtainGrids != null)
            {
                foreach (CurtainGrid g in cs.CurtainGrids)
                    result.Add(g);
                return result;
            }

            // 帷幕屋頂（FootPrintRoof）
            if (element is FootPrintRoof fpRoof && fpRoof.CurtainGrids != null)
            {
                foreach (CurtainGrid g in fpRoof.CurtainGrids)
                    result.Add(g);
                return result;
            }

            // 帷幕屋頂（ExtrusionRoof）
            if (element is ExtrusionRoof exRoof && exRoof.CurtainGrids != null)
            {
                foreach (CurtainGrid g in exRoof.CurtainGrids)
                    result.Add(g);
                return result;
            }

            return result;
        }

        public static string GetElementTypeName(Element element)
        {
            if (element is Wall) return "帷幕牆";
            if (element is FootPrintRoof || element is ExtrusionRoof) return "帷幕屋頂";
            if (element is CurtainSystem) return "帷幕系統";
            return "帷幕元素";
        }
    }

    /// <summary>
    /// 選取篩選器：只允許選取零件 (Parts)
    /// </summary>
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
