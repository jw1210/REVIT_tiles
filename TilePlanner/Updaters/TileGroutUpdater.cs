using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TilePlanner.Core;
using TilePlanner.Core.Services;
using TilePlanner.Core.Utils;

namespace TilePlanner.Updaters
{
    public class TileGroutUpdater : IUpdater
    {
        private static UpdaterId _updaterId;
        private static bool _isProcessing = false;

        public TileGroutUpdater(AddInId addinId)
        {
            _updaterId = new UpdaterId(addinId, new Guid("7D3B5CDE-8A9B-4C0F-BDD1-E8A9B4C0FBDD"));
        }

        public void Execute(UpdaterData data)
        {
            if (_isProcessing) return;

            Document doc = data.GetDocument();
            ICollection<ElementId> modifiedIds = data.GetModifiedElementIds();
            if (modifiedIds.Count == 0) return;

            Element firstElem = doc.GetElement(modifiedIds.First());
            if (firstElem == null || !(firstElem is Part part)) return;

            // [備註] V4.5.2 診斷點：若此處無彈窗，代表 Revit 未觸發 IUpdater

            // 1. 優先嘗試從性質欄取得 Host ID
            Element host = null;
            Parameter pHost = firstElem.LookupParameter(ParameterService.PARAM_HOST_ID);
            if (pHost != null && !string.IsNullOrEmpty(pHost.AsString()))
            {
                if (long.TryParse(pHost.AsString(), out long idLong))
                {
                    host = doc.GetElement(new ElementId(idLong));
                }
            }

            // 2. 若性質欄失效，嘗試從 Revit 內建溯源取得 (Fallback)
            if (host == null)
            {
                host = part.GetHostWall(); // 使用 RevitElementExtensions 中的擴充方法
            }

            if (host == null) return;

                // 從目前零件讀取新的 Config
                TileConfig newConfig = ParameterService.GetConfigFromElement(firstElem);

                try
                {
                    _isProcessing = true;
                    // 執行重新分割 (TilePartEngine 會先 ClearOldGridElements)
                    TilePartEngine engine = new TilePartEngine(doc, newConfig);
                    engine.ExecuteOnElement(host);
                    
                    // 重新寫入參數到新生成的零件
                    var newParts = PartUtils.GetAssociatedParts(doc, host.Id, false, true);
                    foreach (ElementId newPartId in newParts)
                    {
                        Element newPart = doc.GetElement(newPartId);
                        ParameterService.SetConfigParams(newPart, newConfig, host.Id.ToString());
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("TilePlanner Updater Error", ex.Message + "\n" + ex.StackTrace);
                }
                finally
                {
                    _isProcessing = false;
                }
        }

        public string GetAdditionalInformation() => "自動更新磁磚灰縫間距並重新分割牆面";
        public ChangePriority GetChangePriority() => ChangePriority.Structure;
        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "TileGroutUpdater";
    }
}
