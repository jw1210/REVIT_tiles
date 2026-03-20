using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using TilePlanner.Core;
using TilePlanner.Core.Services;

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
            if (_isProcessing) return; // 防止循環觸發

            Document doc = data.GetDocument();
            
            // 取得被修改的零件
            ICollection<ElementId> modifiedIds = data.GetModifiedElementIds();
            if (modifiedIds.Count == 0) return;

            Element firstElem = doc.GetElement(modifiedIds.First());
            if (firstElem == null) return;

            // 取得對應的 Host ID
            Parameter pHost = firstElem.LookupParameter(ParameterService.PARAM_HOST_ID);
            if (pHost == null || string.IsNullOrEmpty(pHost.AsString())) return;

            if (long.TryParse(pHost.AsString(), out long hostIdLong))
            {
                ElementId hostId = new ElementId(hostIdLong);
                Element host = doc.GetElement(hostId);
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
                catch (Exception)
                {
                    // 可以記錄錯誤
                }
                finally
                {
                    _isProcessing = false;
                }
            }
        }

        public string GetAdditionalInformation() => "自動更新磁磚灰縫間距並重新分割牆面";
        public ChangePriority GetChangePriority() => ChangePriority.Structure;
        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "TileGroutUpdater";
    }
}
