using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using TilePlanner.Core;

namespace TilePlanner
{
    /// <summary>
    /// Revit 外部應用程式 — 建立 Ribbon UI 與註冊 Updater
    /// </summary>
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 建立 Ribbon 標籤
                string tabName = "磁磚計畫";
                application.CreateRibbonTab(tabName);

                // 建立 Ribbon 面板
                RibbonPanel panel = application.CreateRibbonPanel(tabName, "磁磚工具");

                // 取得 Assembly 路徑
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                // ===== 建立磁磚計畫按鈕 =====
                PushButtonData createBtnData = new PushButtonData(
                    "CreateTilePlan",
                    "建立\n磁磚計畫",
                    assemblyPath,
                    "TilePlanner.Commands.CreateTilePlanCommand");
                createBtnData.ToolTip = "在選取的帷幕牆上建立磁磚分割計畫";
                createBtnData.LongDescription =
                    "選取一面已建立的帷幕牆（無分割），設定磁磚尺寸、灰縫寬度、排列模式等參數，自動在帷幕牆上建立磁磚分割格線與竪框灰縫。";
                PushButton createBtn = panel.AddItem(createBtnData) as PushButton;

                // ===== 移除磁磚計畫按鈕 =====
                PushButtonData removeBtnData = new PushButtonData(
                    "RemoveTilePlan",
                    "移除\n磁磚計畫",
                    assemblyPath,
                    "TilePlanner.Commands.RemoveTilePlanCommand");
                removeBtnData.ToolTip = "移除選取帷幕牆上的磁磚分割";
                removeBtnData.LongDescription =
                    "清除帷幕牆上的所有格線與竪框，還原為無分割狀態。";
                PushButton removeBtn = panel.AddItem(removeBtnData) as PushButton;

                // ===== 格線整體連動按鈕 =====
                PushButtonData toggleBtnData = new PushButtonData(
                    "ToggleLinkedMove",
                    "整體\n連動",
                    assemblyPath,
                    "TilePlanner.Commands.ToggleLinkedMoveCommand");
                toggleBtnData.ToolTip = "切換格線整體連動功能";
                toggleBtnData.LongDescription =
                    "啟用後，移動一條分割格線時，同方向的所有格線將一起平移，保持磁磚排列間距。";
                PushButton toggleBtn = panel.AddItem(toggleBtnData) as PushButton;

                // 註冊 Dynamic Model Updater（在文件開啟時才實際生效）
                application.ControlledApplication.DocumentOpened += (sender, args) =>
                {
                    TileGridUpdater.Register(args.Document);
                    TileAutoUpdater.Register(args.Document);
                };

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
