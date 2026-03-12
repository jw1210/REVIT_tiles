using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core
{
    /// <summary>
    /// 網格約束管理器 — 實現連續尺寸標註鎖定與整體平移機制 (V2.4)
    /// 
    /// 責務：
    /// 1. 將同方向的參照平面分組為 ReferenceArray
    /// 2. 建立 Multi-Segment Dimension（連續尺寸標註）
    /// 3. 將每個段落 (DimensionSegment) 強制上鎖，達成「拉動一條線，整張網子跟著平移」的效果
    /// 4. 隱藏標註以保持檢視幹淨
    /// </summary>
    public class GridConstraintManager
    {
        private readonly Document _doc;
        private readonly PlanarFace _targetFace;
        private readonly TileConfig _config;

        public GridConstraintManager(Document doc, PlanarFace targetFace, TileConfig config)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _targetFace = targetFace; // 允許 null（用於清除模式）
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 鎖定所有同方向的參照平面，並建立連續尺寸標註
        /// </summary>
        /// <param name="planeIds">參照平面的 ElementId 列表</param>
        /// <param name="isHorizontal">true=水平方向, false=垂直方向</param>
        public List<ElementId> LockPlanes(List<ElementId> planeIds, bool isHorizontal)
        {
            var result = new List<ElementId>();

            // 檢查必要參數
            if (_targetFace == null)
            {
                System.Diagnostics.Debug.WriteLine("[GridConstraintManager] _targetFace 為 null，無法鎖定平面");
                return result;
            }

            // 需要至少 2 條線才能建立有意義的連續標註
            if (planeIds == null || planeIds.Count < 2)
                return result;

            // 依據方向進行排序 (確保連續標註的段落順序正確)
            XYZ sortAxis = isHorizontal ? _targetFace.YVector : _targetFace.XVector;
            var sortedIds = planeIds
                .OrderBy(id =>
                {
                    var rp = _doc.GetElement(id) as ReferencePlane;
                    if (rp == null) return double.MinValue;
                    // 以方向投影值排序
                    return rp.BubbleEnd.DotProduct(sortAxis);
                })
                .ToList();

            // ✨ 【核心禁令】：ReferenceArray 僅包含程式生成的參考線，嚴禁包含宿主邊界
            ReferenceArray refArray = new ReferenceArray();
            foreach (var id in sortedIds)
            {
                var rp = _doc.GetElement(id) as ReferencePlane;
                if (rp != null)
                {
                    refArray.Append(rp.GetReference());
                }
            }

            // 建立連續尺寸標註
            if (refArray.Size >= 2)
            {
                try
                {
                    // [V3.3 UX] 建立放置標註用的虛擬線 (避開幾何干擾)
                    // 往下或往左退約 60 公分 (-2.0 feet)
                    double retreatOffset = -2.0; 
                    BoundingBoxUV bbox = _targetFace.GetBoundingBox();

                    // [V3.3.3 Bugfix] 修正標註線方向：標註線必須與被標註的平面垂直
                    XYZ p1, p2;
                    if (isHorizontal)
                    {
                        // 被標註的是水平面 (Parallel to X)，標註線必須是垂直方向 (Parallel to Y)
                        // 這樣才能測量 Y 軸方向的間距
                        p1 = _targetFace.Origin
                            + (bbox.Min.U + 1.0) * _targetFace.XVector // 往右偏一點避開邊界
                            + (bbox.Min.V + retreatOffset) * _targetFace.YVector;
                        p2 = p1 + (10.0 * _targetFace.YVector); // 方向改為 Y
                    }
                    else
                    {
                        // 被標註的是垂直面 (Parallel to Y)，標註線必須是水平方向 (Parallel to X)
                        p1 = _targetFace.Origin
                            + (bbox.Min.U + retreatOffset) * _targetFace.XVector
                            + (bbox.Min.V + 1.0) * _targetFace.YVector; // 往上偏一點
                        p2 = p1 + (10.0 * _targetFace.XVector); // 方向改為 X
                    }

                    Line dimLine = Line.CreateBound(p1, p2);

                    // 1. 建立標註
                    Dimension gridDimension = _doc.Create.NewDimension(_doc.ActiveView, dimLine, refArray);

                    if (gridDimension != null)
                    {
                        // 2. 【核心鎖鏈】：強制標註的每一個段落上鎖
                        // 這會鎖定「每兩條線之間的間距」，所以移動一條，全體都會平移
                        if (gridDimension.Segments.Size > 0)
                        {
                            foreach (DimensionSegment seg in gridDimension.Segments)
                            {
                                seg.IsLocked = true;
                            }
                        }
                        else
                        {
                            gridDimension.IsLocked = true;
                        }

                        // 3. 【障眼法】：立即從當前視圖隱藏標註
                        try
                        {
                            _doc.ActiveView.HideElements(new List<ElementId> { gridDimension.Id });
                        }
                        catch
                        {
                            // 隱藏失敗通常是因為該品類已被隱藏，不影響功能
                        }

                        result.Add(gridDimension.Id);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GridConstraintManager] 建立隱形鎖鏈失敗: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// 刪除指定的連續標註（通常用於重繪時清除舊標註）
        /// </summary>
        public void RemoveLockingDimensions(List<ElementId> dimensionIds)
        {
            if (dimensionIds == null || dimensionIds.Count == 0)
                return;

            foreach (var id in dimensionIds)
            {
                try
                {
                    Element elem = _doc.GetElement(id);
                    if (elem != null)
                    {
                        _doc.Delete(id);
                    }
                }
                catch
                {
                    // 若刪除失敗可能是已被刪除，略過
                }
            }
        }

        /// <summary>
        /// 尋找並刪除某個宿主相關的所有舊網格標註
        /// （用於「一鍵重繪」時清除舊標註，防止約束衝突）
        /// </summary>
        public void ClearOldConstraintDimensions(ElementId hostElementId)
        {
            var allDimensions = new FilteredElementCollector(_doc)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .Where(d => d != null && d.References != null)
                .ToList();

            var toDelete = new List<ElementId>();

            foreach (var dim in allDimensions)
            {
                // 檢查標註是否涉及「磁磚切割網格」子品類的參照平面
                bool isGridDimension = false;
                try
                {
                    for (int i = 0; i < dim.References.Size; i++)
                    {
                        Reference rf = dim.References.get_Item(i);
                        Element refElem = _doc.GetElement(rf.ElementId);
                        if (refElem is ReferencePlane rp)
                        {
                            // 檢查名稱是否包含該宿主的磁磚網格標記
                            if (rp.Name.Contains($"TileGrid_") && rp.Name.Contains(hostElementId.ToString()))
                            {
                                isGridDimension = true;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // 略過無法檢查的標註
                    continue;
                }

                if (isGridDimension)
                {
                    toDelete.Add(dim.Id);
                }
            }

            // 刪除所有舊標註
            foreach (var id in toDelete)
            {
                try
                {
                    _doc.Delete(id);
                }
                catch
                {
                    // 若刪除失敗略過
                }
            }
        }
    }
}
