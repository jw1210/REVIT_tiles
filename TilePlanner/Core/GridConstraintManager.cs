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

            // 依據方向進行排序
            XYZ sortAxis = isHorizontal ? _targetFace.YVector : _targetFace.XVector;
            var sortedIds = planeIds
                .OrderBy(id =>
                {
                    var rp = _doc.GetElement(id) as ReferencePlane;
                    if (rp == null) return double.MinValue;
                    return rp.BubbleEnd.DotProduct(sortAxis);
                })
                .ToList();

            // 建立 ReferenceArray（包含所有該方向的參照平面）
            ReferenceArray refArray = new ReferenceArray();
            foreach (var id in sortedIds)
            {
                var rp = _doc.GetElement(id) as ReferencePlane;
                if (rp != null)
                {
                    refArray.Append(rp.GetReference());
                }
            }

            // 嘗試建立連續尺寸標註
            if (refArray.Size >= 2)
            {
                try
                {
                    // 計算標註線的位置（在目標面外延伸 500mm 避免與幾何重疊）
                    double extensionLength = 500.0 / 304.8; // 轉換為 feet
                    BoundingBoxUV bbox = _targetFace.GetBoundingBox();

                    XYZ p1, p2;
                    if (isHorizontal)
                    {
                        // 水平標註線沿 X 方向，位置在 Y 的下方（宿主面外）
                        p1 = _targetFace.Origin
                            + (bbox.Min.U) * _targetFace.XVector
                            + (bbox.Min.V - extensionLength) * _targetFace.YVector;
                        p2 = _targetFace.Origin
                            + (bbox.Max.U) * _targetFace.XVector
                            + (bbox.Min.V - extensionLength) * _targetFace.YVector;
                    }
                    else
                    {
                        // 垂直標註線沿 Y 方向，位置在 X 的左方（宿主面外）
                        p1 = _targetFace.Origin
                            + (bbox.Min.U - extensionLength) * _targetFace.XVector
                            + (bbox.Min.V) * _targetFace.YVector;
                        p2 = _targetFace.Origin
                            + (bbox.Min.U - extensionLength) * _targetFace.XVector
                            + (bbox.Max.V) * _targetFace.YVector;
                    }

                    // 建立尺寸標註線
                    Line dimLine = Line.CreateBound(p1, p2);

                    // 呼叫 API 建立連續尺寸標註
                    Dimension gridDimension = _doc.Create.NewDimension(_doc.ActiveView, dimLine, refArray);

                    if (gridDimension != null)
                    {
                        // 強制鎖定每一個段落 (DimensionSegment)
                        if (gridDimension.Segments.Size > 0)
                        {
                            foreach (DimensionSegment seg in gridDimension.Segments)
                            {
                                seg.IsLocked = true; // 將每個間距鎖死
                            }
                        }
                        else
                        {
                            // 萬一只有兩條線（沒有 Segment），則鎖整條標註
                            gridDimension.IsLocked = true;
                        }

                        // 隱藏標註，保持檢視幹淨（可選）
                        try
                        {
                            _doc.ActiveView.HideElements(new List<ElementId> { gridDimension.Id });
                        }
                        catch
                        {
                            // 若隱藏失敗，不影響整個流程
                        }

                        result.Add(gridDimension.Id);
                    }
                }
                catch (Exception ex)
                {
                    // 記錄錯誤但不中斷整個流程
                    System.Diagnostics.Debug.WriteLine($"[GridConstraintManager] 建立連續標註失敗: {ex.Message}");
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
