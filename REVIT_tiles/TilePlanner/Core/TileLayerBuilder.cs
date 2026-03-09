using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core
{
    public static class TileLayerBuilder
    {
        /// <summary>
        /// 在主牆體表面建立一片帷幕牆當作磁磚層 (Option A)
        /// 如果選取的已經是帷幕牆，則直接回傳原牆
        /// </summary>
        public static Wall GetOrCreateTileLayer(Document doc, Wall hostWall, out bool isNewLayer)
        {
            isNewLayer = false;

            // 1. 如果本身就是帷幕牆，或者已經是生成的 Tile Layer
            if (hostWall.WallType.Kind == WallKind.Curtain || TileDataManager.HasTileConfig(hostWall))
            {
                return hostWall;
            }

            // 2. 獲取或建立乾淨無分割的帷幕牆類型
            WallType curtainWallType = GetCleanCurtainWallType(doc);

            // 3. 取得主牆體的線條與參數
            LocationCurve locCurve = hostWall.Location as LocationCurve;
            if (locCurve == null) throw new InvalidOperationException("無法取得牆體位置。");

            Curve curve = locCurve.Curve;
            
            // 嘗試找到外部面（這裡簡化處理：直接沿用牆中心線稍微往外偏移，或者讓使用者自己調）
            // 在此示範中，我們先將新帷幕牆建在原本的位置，並利用參數調整位移 (Offset)
            double wallWidth = hostWall.WallType.Width;
            
            // 往外偏移一半牆厚加上一點點容差避免 Z-fighting (Revit 單位為英尺)
            // 使用者要求：先取消磁磚厚度，因此只保留 1mm 作為 Z-fighting 防止
            double offsetDist = (wallWidth / 2.0) + (1.0 / 304.8); 

            // 將曲線向法線方向平移
            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);
            XYZ dir = (p1 - p0).Normalize();
            // 基本牆通常向上 (Z)，法向量(外側) = Z 交叉 dir
            XYZ normal = XYZ.BasisZ.CrossProduct(dir).Normalize();

            // 若要支援翻轉，可判斷 hostWall.Flipped
            if (hostWall.Flipped) normal = -normal;

            Transform transform = Transform.CreateTranslation(normal * offsetDist);
            Curve offsetCurve = curve.CreateTransformed(transform);

            // 4. 建立薄薄的帷幕牆 (磁磚層)
            ElementId levelId = hostWall.LevelId;
            double height = hostWall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() 
                         ?? 10.0; // 預設 10 呎

            Wall tileLayer = Wall.Create(doc, offsetCurve, curtainWallType.Id, levelId, height, 0.0, false, false);
            
            // 繼承主牆的底部與頂部偏移
            var baseOffset = hostWall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            if (baseOffset != null) tileLayer.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.Set(baseOffset.AsDouble());
            
            var topOffset = hostWall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
            if (topOffset != null) tileLayer.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.Set(topOffset.AsDouble());

            var topConstraint = hostWall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
            if (topConstraint != null) tileLayer.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(topConstraint.AsElementId());

            isNewLayer = true;
            return tileLayer;
        }

        private static WallType GetCleanCurtainWallType(Document doc)
        {
            string typeName = "TilePlanner_CleanLayer";

            WallType existingType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Name == typeName);

            if (existingType != null) return existingType;

            // Find any curtain wall type to duplicate
            WallType baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Kind == WallKind.Curtain && wt.Name.Contains("無分割"));

            if (baseType == null)
            {
                baseType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(wt => wt.Kind == WallKind.Curtain);
            }

            if (baseType == null)
                throw new InvalidOperationException("專案中沒有任何帷幕牆類型可以作為磁磚層使用。");

            WallType newType = baseType.Duplicate(typeName) as WallType;
            if (newType != null)
            {
                // Set layout to None (0)
                try { newType.get_Parameter(BuiltInParameter.SPACING_LAYOUT_HORIZ)?.Set(0); } catch { }
                try { newType.get_Parameter(BuiltInParameter.SPACING_LAYOUT_VERT)?.Set(0); } catch { }

                // Clear auto mullions
                try { newType.get_Parameter(BuiltInParameter.AUTO_MULLION_INTERIOR_VERT)?.Set(ElementId.InvalidElementId); } catch { }
                try { newType.get_Parameter(BuiltInParameter.AUTO_MULLION_INTERIOR_HORIZ)?.Set(ElementId.InvalidElementId); } catch { }
                try { newType.get_Parameter(BuiltInParameter.AUTO_MULLION_BORDER1_VERT)?.Set(ElementId.InvalidElementId); } catch { }
                try { newType.get_Parameter(BuiltInParameter.AUTO_MULLION_BORDER2_VERT)?.Set(ElementId.InvalidElementId); } catch { }
                try { newType.get_Parameter(BuiltInParameter.AUTO_MULLION_BORDER1_HORIZ)?.Set(ElementId.InvalidElementId); } catch { }
                try { newType.get_Parameter(BuiltInParameter.AUTO_MULLION_BORDER2_HORIZ)?.Set(ElementId.InvalidElementId); } catch { }
            }

            return newType;
        }
    }
}
