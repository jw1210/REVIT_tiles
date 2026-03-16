using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace TilePlanner
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string tabName = "磁磚計畫";
                application.CreateRibbonTab(tabName);
                RibbonPanel panel = application.CreateRibbonPanel(tabName, "磁磚工具");
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                // 建立與移除按鈕
                PushButtonData createBtnData = new PushButtonData("CreateTilePlan", "建立\n磁磚計畫", assemblyPath, "TilePlanner.Commands.CreateTilePlanCommand");
                createBtnData.ToolTip = "選取牆面、樓板後自動產生成 3D 磁磚計畫 (V3.8 支援批次多選)";
                panel.AddItem(createBtnData);

                PushButtonData removeBtnData = new PushButtonData("RemoveTilePlan", "移除\n磁磚計畫", assemblyPath, "TilePlanner.Commands.RemoveTilePlanCommand");
                panel.AddItem(removeBtnData);

                // 整體連動按鈕
                PushButtonData toggleBtnData = new PushButtonData("ToggleLinkedMove", "整體\n連動", assemblyPath, "TilePlanner.Commands.ToggleLinkedMoveCommand");
                panel.AddItem(toggleBtnData);

                // ==========================================
                // [V3.1] 獨立的大按鈕：顯示/隱藏 網格
                // ==========================================
                PushButtonData toggleGridBtnData = new PushButtonData("ToggleGridCommand", "顯示/隱藏\n網格", assemblyPath, "TilePlanner.Commands.ToggleGridCommand");
                toggleGridBtnData.ToolTip = "一鍵隱藏或顯示磁磚切割輔助綠線";
                panel.AddItem(toggleGridBtnData);

                // ==========================================
                // [V3.1] 獨立的大按鈕：顯示/隱藏 零件
                // ==========================================
                PushButtonData togglePartsBtnData = new PushButtonData("TogglePartsVisibility", "顯示/隱藏\n零件", assemblyPath, "TilePlanner.Commands.TogglePartsVisibilityCommand");
                togglePartsBtnData.ToolTip = "快速切換原主體(Wall)與切割好的磁磚零件(Parts)";
                panel.AddItem(togglePartsBtnData);

                // ==========================================
                // [V4.1.3] 手動轉角接合 - 零件延伸與平面分割版
                // ==========================================
                PushButtonData manualJoinBtnData = new PushButtonData(
                    "ManualCornerJoin", 
                    "手動轉角接合\n(V4.1.3)", 
                    assemblyPath, 
                    "TilePlanner.Commands.ManualCornerJoinCommand");
                manualJoinBtnData.ToolTip = "[V4.1.3 零件延伸/平面切割] 透過零件物理延伸與無限平面切割技術，確保轉角接合之幾何穩定性。";
                panel.AddItem(manualJoinBtnData);

                return Result.Succeeded;
            }
            catch (Exception)
            {
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
