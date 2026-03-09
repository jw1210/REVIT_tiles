using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace TilePlanner.Core
{
    /// <summary>
    /// 自動延伸更新器
    /// 監聽帶有 TileConfig 的元素 (磁磚層)。如果其幾何尺寸被修改 (如拉長、編輯輪廓)
    /// 則自動讀取先前的設定，清除舊格線並重新生成新的磁磚分割，達到「自動延伸補足」的效果。
    /// </summary>
    public class TileAutoUpdater : IUpdater
    {
        public static bool IsEnabled { get; set; } = true;

        // 吸收因為自己更新而造成的第二波觸發
        private static HashSet<ElementId> _recentlyUpdated = new HashSet<ElementId>();
        
        // 紀錄元素的實質幾何特徵 (面積_長度)，用來過濾掉「只移動格線卻被判定為幾何改變」的無效觸發
        private static Dictionary<ElementId, string> _geometrySignatures = new Dictionary<ElementId, string>();

        public static void UpdateSignatureCache(Element elem)
        {
            if (elem != null)
                _geometrySignatures[elem.Id] = GetGeometrySignature(elem);
        }

        private static string GetGeometrySignature(Element elem)
        {
            double area = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
            double length = elem.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
            return $"{area:F4}_{length:F4}";
        }

        private static readonly UpdaterId _updaterId =
            new UpdaterId(new AddInId(new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")),
                          new Guid("F9E8D7C6-B5A4-3210-9876-543210FEDCBA"));

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "TileAutoUpdater";
        public string GetAdditionalInformation() => "當磁磚層尺寸改變時自動延伸補足格線";
        public ChangePriority GetChangePriority() => ChangePriority.GridsLevelsReferencePlanes;

        public static void Register(Document doc)
        {
            TileAutoUpdater updater = new TileAutoUpdater();
            if (!UpdaterRegistry.IsUpdaterRegistered(updater.GetUpdaterId()))
            {
                UpdaterRegistry.RegisterUpdater(updater, doc, true);
                
                // 監聽牆面、帷幕屋頂等幾何改變
                var filter = new ElementCategoryFilter(BuiltInCategory.OST_Walls);
                UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), doc, filter, Element.GetChangeTypeGeometry());
            }
        }

        public void Execute(UpdaterData data)
        {
            if (!IsEnabled) return;

            Document doc = data.GetDocument();
            ICollection<ElementId> modifiedIds = data.GetModifiedElementIds();

            foreach (ElementId id in modifiedIds)
            {
                Element elem = doc.GetElement(id);
                if (elem == null) continue;

                // 檢查是否是我們的磁磚層 (有沒有存 TileConfig)
                if (!TileDataManager.HasTileConfig(elem)) continue;

                // 讀取設定
                TileConfig config = TileDataManager.LoadTileConfig(elem, out ElementId hostId);
                if (config == null) continue;

                // 防禦機制 1：自己剛剛才更新過，略過這個 Echo 觸發
                if (_recentlyUpdated.Contains(id))
                {
                    _recentlyUpdated.Remove(id);
                    continue;
                }

                // 防禦機制 2：實質幾何（面積與長度）根本沒變，代表只是格線被移動或其他無關緊要的改變，忽略！
                string currentSig = GetGeometrySignature(elem);
                if (_geometrySignatures.TryGetValue(id, out string knownSig) && knownSig == currentSig)
                {
                    continue;
                }

                // 若確實改變了，更新紀錄
                _geometrySignatures[id] = currentSig;

                try
                {
                    var grids = Commands.CurtainGridHelper.GetCurtainGrids(elem);
                    if (grids.Count == 0) continue;

                    // == 為了避免無限迴圈，暫時關閉 TileAutoUpdater 自身的重新觸發 ==
                    IsEnabled = false;

                    try
                    {
                        // 關閉另一支 Updater
                        bool wasLinkedMoveEnabled = TileGridUpdater.IsEnabled;
                        TileGridUpdater.IsEnabled = false;

                    // 清除舊格線
                    foreach (CurtainGrid g in grids)
                    {
                        ClearGridContent(doc, g);
                    }
                    doc.Regenerate(); // 必須 Regenerate 才能讓舊格線徹底消失

                    // 重新生成格線
                    TileLayoutEngine engine = new TileLayoutEngine(doc, config);
                    if (elem is Wall wall)
                    {
                        engine.ExecuteOnWall(wall);
                    }
                    else
                    {
                        foreach (CurtainGrid g in grids)
                        {
                            engine.ExecuteOnGrid(g, elem);
                        }
                    }

                        TileGridUpdater.CaptureGridLinePositions(doc, elem);
                        TileGridUpdater.IsEnabled = wasLinkedMoveEnabled;

                        // 更新完成後，登記到最近更新名單中，吸收下一波的反彈觸發
                        _recentlyUpdated.Add(id);
                    }
                    finally
                    {
                        // 確保 AutoUpdater 解鎖
                        IsEnabled = true;
                    }
                }
                catch (Exception)
                {
                    IsEnabled = true;
                    // 若自動更新失敗，不中斷使用者的原本操作
                }
            }
        }

        private void ClearGridContent(Document doc, CurtainGrid grid)
        {
            List<ElementId> toDelete = new List<ElementId>();

            ICollection<ElementId> mullionIds = grid.GetMullionIds();
            foreach (ElementId mId in mullionIds)
            {
                Mullion m = doc.GetElement(mId) as Mullion;
                if (m != null && m.Pinned) m.Pinned = false;
                toDelete.Add(mId);
            }

            foreach (ElementId uId in grid.GetUGridLineIds())
            {
                CurtainGridLine gl = doc.GetElement(uId) as CurtainGridLine;
                if (gl != null && gl.Pinned) gl.Pinned = false;
                toDelete.Add(uId);
            }

            foreach (ElementId vId in grid.GetVGridLineIds())
            {
                CurtainGridLine gl = doc.GetElement(vId) as CurtainGridLine;
                if (gl != null && gl.Pinned) gl.Pinned = false;
                toDelete.Add(vId);
            }

            if (toDelete.Count > 0)
            {
                try
                {
                    doc.Delete(toDelete);
                }
                catch (Exception) { }
            }
        }
    }
}
