using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core
{
    public class GridConstraintManager
    {
        private readonly Document _doc;
        private readonly PlanarFace _targetFace;
        private readonly TileConfig _config;

        public GridConstraintManager(Document doc, PlanarFace targetFace, TileConfig config)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _targetFace = targetFace; 
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public List<ElementId> LockPlanes(List<ElementId> planeIds, bool isHorizontal)
        {
            var result = new List<ElementId>();

            // [V2.5 防呆] 禁止於 3D 視圖建立標註導致崩潰
            if (_doc.ActiveView.ViewType == ViewType.ThreeD) return result; 

            if (_targetFace == null || planeIds == null || planeIds.Count < 2) return result;

            XYZ sortAxis = isHorizontal ? _targetFace.YVector : _targetFace.XVector;
            var sortedIds = planeIds
                .Select(id => _doc.GetElement(id) as ReferencePlane)
                .Where(rp => rp != null)
                .OrderBy(rp => rp.BubbleEnd.DotProduct(sortAxis)).ToList();

            ReferenceArray refArray = new ReferenceArray();
            foreach (var rp in sortedIds) refArray.Append(rp.GetReference());

            if (refArray.Size >= 2)
            {
                try
                {
                    BoundingBoxUV bbox = _targetFace.GetBoundingBox();
                    XYZ p1, p2;

                    // [V2.5 修正] 標註線兩端大幅延伸，確保貫穿所有參考平面以建立整體連動
                    if (isHorizontal)
                    {
                        p1 = _targetFace.Origin + (bbox.Min.U - 2.0) * _targetFace.XVector + (bbox.Min.V - 10.0) * _targetFace.YVector;
                        p2 = _targetFace.Origin + (bbox.Min.U - 2.0) * _targetFace.XVector + (bbox.Max.V + 10.0) * _targetFace.YVector;
                    }
                    else
                    {
                        p1 = _targetFace.Origin + (bbox.Min.U - 10.0) * _targetFace.XVector + (bbox.Min.V - 2.0) * _targetFace.YVector;
                        p2 = _targetFace.Origin + (bbox.Max.U + 10.0) * _targetFace.XVector + (bbox.Min.V - 2.0) * _targetFace.YVector;
                    }

                    Line dimLine = Line.CreateBound(p1, p2);
                    Dimension gridDimension = _doc.Create.NewDimension(_doc.ActiveView, dimLine, refArray);

                    if (gridDimension != null)
                    {
                        if (gridDimension.Segments.Size > 0)
                        {
                            foreach (DimensionSegment seg in gridDimension.Segments) seg.IsLocked = true;
                        }
                        else
                        {
                            gridDimension.IsLocked = true;
                        }
                        try { _doc.ActiveView.HideElements(new List<ElementId> { gridDimension.Id }); } catch { }
                        result.Add(gridDimension.Id);
                    }
                }
                catch (Exception) { /* 忽略約束失敗 */ }
            }
            return result;
        }

        public void ClearOldConstraintDimensions(string hostIdString)
        {
            // [V2.5 效能] 限制在當前視圖搜尋
            var allDimensions = new FilteredElementCollector(_doc, _doc.ActiveView.Id)
                .OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => d != null && d.References != null).ToList();

            var toDelete = new List<ElementId>();
            foreach (var dim in allDimensions)
            {
                bool isGridDimension = false;
                try
                {
                    for (int i = 0; i < dim.References.Size; i++)
                    {
                        if (_doc.GetElement(dim.References.get_Item(i).ElementId) is ReferencePlane rp)
                        {
                            if (rp.Name.Contains("TileGrid_") && rp.Name.Contains(hostIdString))
                            {
                                isGridDimension = true;
                                break;
                            }
                        }
                    }
                }
                catch { continue; }
                if (isGridDimension) toDelete.Add(dim.Id);
            }
            foreach (var id in toDelete) { try { _doc.Delete(id); } catch { } }
        }
    }
}
