using System;
using System.Collections.Generic;
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
                        new CurtainElementSelectionFilter(),
                        "請選取要進行磁磚分割的帷幕牆、帷幕系統或帷幕屋頂");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
                Element selectedElement = doc.GetElement(elemRef.ElementId);



                // 先不檢查 grids，等交易內建立了 Tile Layer 再取。
                // 開啟參數設定對話框
                TilePlannerDialog dialog = new TilePlannerDialog();
                if (dialog.ShowDialog() != true)
                    return Result.Cancelled;

                TileConfig config = dialog.GetTileConfig();

                // 確保 Updater 已在目前文件中註冊 (這裡可以考慮稍後移除 TileGridUpdater)
                TileGridUpdater.Register(doc);
                TileAutoUpdater.Register(doc);

                using (Transaction trans = new Transaction(doc, "建立磁磚零件計畫"))
                {
                    trans.Start();

                    try
                    {
                        // 避免在建立過程觸發其他事件
                        TileAutoUpdater.IsEnabled = false;

                        // 直接對選取的目標實體牆面/樓板進行 Parts 分割
                        TilePartEngine engine = new TilePartEngine(doc, config);
                        engine.ExecuteOnElement(selectedElement);

                        // 將設定存入 Element 中 (支援自動延伸更新)
                        TileDataManager.SaveTileConfig(selectedElement, config, selectedElement.Id);

                        trans.Commit();
                        TileGridUpdater.IsEnabled = true;

                        TaskDialog.Show("磁磚計畫",
                            $"實體磁磚零件 (Parts) 產生並分割完成！\n\n" +
                            $"📍 已自動生成真實厚度灰縫。\n" +
                            $"💡 提示：如果視圖中看不到分割，請確認該視圖的屬性面板中，「零件可見性(Parts Visibility)」已經設定為「展示零件(Show Parts)」。\n");

                        // 將最初始的實質幾何特徵寫入快取
                        TileAutoUpdater.UpdateSignatureCache(selectedElement);
                        TileAutoUpdater.IsEnabled = true;
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        TileAutoUpdater.IsEnabled = true;
                        trans.RollBack();
                        message = $"磁磚分割失敗：{ex.Message}\n\n{ex.StackTrace}";
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
    /// 選取篩選器：允許帷幕牆、帷幕系統、帷幕屋頂
    /// </summary>
    public class CurtainElementSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            // 允許牆體 (包含帷幕牆) 與樓板
            if (elem is Wall)
                return true;

            if (elem is Floor)
                return true;

            if (elem is Ceiling)
                return true;

            if (elem is RoofBase)
                return true;

            return false;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    // 向後相容
    public class CurtainWallSelectionFilter : CurtainElementSelectionFilter { }
}
