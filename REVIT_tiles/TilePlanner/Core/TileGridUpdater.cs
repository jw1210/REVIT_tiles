using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core
{
    /// <summary>
    /// 磁磚格線整體連動更新器
    /// 移動一條格線 → 所有同方向格線一起平移相同距離
    /// 
    /// 監控所有帷幕格線類別：牆面、屋頂、帷幕系統
    /// </summary>
    public class TileGridUpdater : IUpdater
    {
        private static readonly UpdaterId _updaterId =
            new UpdaterId(new AddInId(new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")),
                          new Guid("D4E5F6A7-B8C9-0123-4567-890ABCDEF012"));

        private static bool _isEnabled = false;
        private static bool _isProcessing = false;

        public static bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        // 記錄每條格線的 3D 中點
        private static Dictionary<ElementId, XYZ> _lastPositions =
            new Dictionary<ElementId, XYZ>();

        // 記錄格線所屬的主體元素 ID
        private static Dictionary<ElementId, ElementId> _gridToHostMap =
            new Dictionary<ElementId, ElementId>();

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "TileGridLinkedUpdater";
        public string GetAdditionalInformation() => "磁磚格線整體連動";
        public ChangePriority GetChangePriority() => ChangePriority.GridsLevelsReferencePlanes;

        public static void Register(Document doc)
        {
            TileGridUpdater updater = new TileGridUpdater();

            if (!UpdaterRegistry.IsUpdaterRegistered(updater.GetUpdaterId()))
            {
                UpdaterRegistry.RegisterUpdater(updater, doc, true);
            }

            // 監控所有帷幕格線類別
            var categories = new[]
            {
                BuiltInCategory.OST_CurtainGrids,
                BuiltInCategory.OST_CurtainGridsWall,
                BuiltInCategory.OST_CurtainGridsRoof,
                BuiltInCategory.OST_CurtainGridsSystem,
                BuiltInCategory.OST_CurtainGridsCurtaSystem
            };

            foreach (var cat in categories)
            {
                try
                {
                    ElementCategoryFilter filter = new ElementCategoryFilter(cat);
                    UpdaterRegistry.AddTrigger(
                        updater.GetUpdaterId(), doc, filter,
                        Element.GetChangeTypeGeometry());
                }
                catch (Exception) { }
            }
        }

        public static void Unregister(Document doc)
        {
            TileGridUpdater updater = new TileGridUpdater();
            if (UpdaterRegistry.IsUpdaterRegistered(updater.GetUpdaterId()))
            {
                UpdaterRegistry.UnregisterUpdater(updater.GetUpdaterId(), doc);
            }
        }

        /// <summary>
        /// 記錄所有格線的 3D 中點
        /// 支援帷幕牆和帷幕系統
        /// </summary>
        public static void CaptureGridLinePositions(Document doc, Element element)
        {
            _lastPositions.Clear();
            _gridToHostMap.Clear();

            List<CurtainGrid> grids = new List<CurtainGrid>();

            if (element is Wall wall && wall.CurtainGrid != null)
                grids.Add(wall.CurtainGrid);
            else if (element is FootPrintRoof fpRoof && fpRoof.CurtainGrids != null)
            {
                foreach (CurtainGrid g in fpRoof.CurtainGrids) grids.Add(g);
            }
            else if (element is ExtrusionRoof exRoof && exRoof.CurtainGrids != null)
            {
                foreach (CurtainGrid g in exRoof.CurtainGrids) grids.Add(g);
            }
            else if (element is CurtainSystem cs && cs.CurtainGrids != null)
            {
                foreach (CurtainGrid g in cs.CurtainGrids) grids.Add(g);
            }

            foreach (CurtainGrid grid in grids)
            {
                foreach (ElementId id in grid.GetUGridLineIds())
                {
                    CurtainGridLine gl = doc.GetElement(id) as CurtainGridLine;
                    if (gl != null)
                    {
                        _lastPositions[id] = GetMidPoint(gl);
                        _gridToHostMap[id] = element.Id;
                    }
                }
                foreach (ElementId id in grid.GetVGridLineIds())
                {
                    CurtainGridLine gl = doc.GetElement(id) as CurtainGridLine;
                    if (gl != null)
                    {
                        _lastPositions[id] = GetMidPoint(gl);
                        _gridToHostMap[id] = element.Id;
                    }
                }
            }
        }

        public static void ClearPositions()
        {
            _lastPositions.Clear();
            _gridToHostMap.Clear();
        }

        public void Execute(UpdaterData data)
        {
            if (!_isEnabled || _isProcessing) return;
            if (_lastPositions.Count == 0) return;

            Document doc = data.GetDocument();
            ICollection<ElementId> modifiedIds = data.GetModifiedElementIds();
            if (modifiedIds.Count == 0) return;

            _isProcessing = true;
            bool wasAutoUpdaterEnabled = TileAutoUpdater.IsEnabled;

            try
            {
                // 執行連動時，禁止 AutoUpdater 被觸發，否則會陷入無限迴圈！
                TileAutoUpdater.IsEnabled = false;

                foreach (ElementId modifiedId in modifiedIds)
                {
                    // 必須是我們追蹤的格線
                    if (!_lastPositions.ContainsKey(modifiedId)) continue;

                    CurtainGridLine movedLine = doc.GetElement(modifiedId) as CurtainGridLine;
                    if (movedLine == null) continue;

                    XYZ oldPos = _lastPositions[modifiedId];
                    XYZ newPos = GetMidPoint(movedLine);
                    XYZ delta = newPos - oldPos;

                    if (delta.GetLength() < 0.0001) continue;

                    // 使用 CurtainGridLine.IsUGridLine 判斷方向
                    bool isU = movedLine.IsUGridLine;

                    // 找到同方向的其他格線並平移
                    List<ElementId> elementsToMove = new List<ElementId>();
                    var otherIds = _lastPositions.Keys
                        .Where(id => !id.Equals(modifiedId))
                        .ToList();

                    foreach (ElementId otherId in otherIds)
                    {
                        CurtainGridLine otherLine = doc.GetElement(otherId) as CurtainGridLine;
                        if (otherLine == null) continue;

                        // 同方向判斷
                        if (otherLine.IsUGridLine != isU) continue;

                        elementsToMove.Add(otherId);
                    }

                    if (elementsToMove.Count > 0)
                    {
                        try
                        {
                            ElementTransformUtils.MoveElements(doc, elementsToMove, delta);
                        }
                        catch (Exception) { }
                    }

                    // 更新所有同方向格線的記錄位置
                    foreach (ElementId id in _lastPositions.Keys.ToList())
                    {
                        CurtainGridLine line = doc.GetElement(id) as CurtainGridLine;
                        if (line == null) continue;
                        if (line.IsUGridLine == isU)
                        {
                            _lastPositions[id] = _lastPositions[id] + delta;
                        }
                    }

                    // ====== 自動為缺口補上新格線 ======
                    if (_gridToHostMap.TryGetValue(modifiedId, out ElementId hostId))
                    {
                        Element hostElem = doc.GetElement(hostId);
                        if (hostElem != null && TileDataManager.HasTileConfig(hostElem))
                        {
                            TileConfig config = TileDataManager.LoadTileConfig(hostElem, out _);
                            if (config != null)
                            {
                                TileLayoutEngine engine = new TileLayoutEngine(doc, config);
                                engine.FillMissingGridLines(hostElem, isU);

                                // 因為新增了格線，必須重新讀取所有格線位置，才能為下一次操作準備
                                CaptureGridLinePositions(doc, hostElem);
                            }
                        }
                    }

                    break; // 只處理第一條
                }
            }
            catch (Exception) { }
            finally
            {
                TileAutoUpdater.IsEnabled = wasAutoUpdaterEnabled;
                _isProcessing = false;
            }
        }

        private static XYZ GetMidPoint(CurtainGridLine gl)
        {
            try { return gl.FullCurve.Evaluate(0.5, true); }
            catch { return XYZ.Zero; }
        }
    }
}
