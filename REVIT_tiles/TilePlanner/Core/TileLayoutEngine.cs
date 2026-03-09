using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TilePlanner.Core
{
    /// <summary>
    /// 磁磚排列核心引擎
    /// 支援帷幕牆 (Wall) 與帷幕系統 (CurtainSystem / 屋頂)
    ///
    /// Revit CurtainGrid 格線方向：
    ///   AddGridLine(true,  pt, false) → U grid line → 水平格線
    ///   AddGridLine(false, pt, false) → V grid line → 垂直格線
    ///
    /// 重要：每次 AddGridLine 後必須呼叫 Document.Regenerate()
    ///       否則後續的 AddGridLine 可能無法正確加入
    /// </summary>
    public class TileLayoutEngine
    {
        private readonly Document _doc;
        private readonly TileConfig _config;

        public TileLayoutEngine(Document doc, TileConfig config)
        {
            _doc = doc;
            _config = config;
        }

        /// <summary>
        /// 安全地加入格線。在小型磁磚 (如二丁掛) 時，屢次 Regenerate 會造成效能崩潰。
        /// 因此改為：先嘗試一般加入，若遭遇 API 失敗（例如點不在面上等誤差），
        /// 才呼叫 _doc.Regenerate() 並重試。
        /// </summary>
        private CurtainGridLine SafeAddGridLine(CurtainGrid grid, bool isU, XYZ pt)
        {
            try
            {
                return grid.AddGridLine(isU, pt, false);
            }
            catch (Exception)
            {
                // 若靜默失敗或拋出例外，執行 Regenerate() 再試一次
                try
                {
                    _doc.Regenerate();
                    return grid.AddGridLine(isU, pt, false);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 在帷幕牆上執行磁磚分割
        /// </summary>
        public void ExecuteOnWall(Wall wall)
        {
            CurtainGrid curtainGrid = wall.CurtainGrid;
            if (curtainGrid == null)
                throw new InvalidOperationException("選取的牆面不是帷幕牆。");

            LocationCurve locCurve = wall.Location as LocationCurve;
            if (locCurve == null)
                throw new InvalidOperationException("無法取得牆面的位置資訊。");

            Curve wallCurve = locCurve.Curve;
            XYZ wallStart = wallCurve.GetEndPoint(0);
            XYZ wallEnd = wallCurve.GetEndPoint(1);
            XYZ wallDir = (wallEnd - wallStart).Normalize();
            double wallLength = wallCurve.Length;
            double wallHeight = GetWallHeight(wall);
            double baseZ = GetWallBaseElevation(wall);

            // ===== Step 1: 水平格線 (true = U grid line) =====
            int numHRows = (int)Math.Floor(wallHeight / _config.CellHeightFeet);
            // 使用曲線中點作為水平格線的參考 XY
            XYZ hMidPoint = wallCurve.Evaluate(0.5, true);

            for (int i = 1; i <= numHRows; i++)
            {
                double zPos = baseZ + i * _config.CellHeightFeet;
                if (zPos >= baseZ + wallHeight - 0.001) break;

                XYZ gridPoint = new XYZ(hMidPoint.X, hMidPoint.Y, zPos);
                try
                {
                    SafeAddGridLine(curtainGrid, true, gridPoint);
                }
                catch (Exception) { }
            }

            // ===== Step 2: 垂直格線 (false = V grid line) =====
            double midZ = baseZ + wallHeight / 2.0;

            if (_config.PatternType == TilePatternType.Grid)
            {
                int numCols = (int)Math.Floor(wallLength / _config.CellWidthFeet);
                for (int i = 1; i <= numCols; i++)
                {
                    double dist = i * _config.CellWidthFeet;
                    if (dist >= wallLength - 0.001) break;

                    // 使用 Curve.Evaluate 取得精準的牆面上的點
                    double normalizedParam = dist / wallLength;
                    XYZ curvePt = wallCurve.Evaluate(normalizedParam, true);
                    XYZ pt = new XYZ(curvePt.X, curvePt.Y, midZ);
                    try
                    {
                        SafeAddGridLine(curtainGrid, false, pt);
                    }
                    catch (Exception) { }
                }
            }
            else
            {
                AddRunningBondVerticals(curtainGrid, wallStart, wallDir,
                    wallLength, baseZ, wallHeight, midZ);
            }


            // ===== Step 3: 竪框灰縫 =====
            // [使用者要求] 暫時移除灰縫功能
            // AddMullionsAsGrout(curtainGrid);
        }

        /// <summary>
        /// 在帷幕系統的 CurtainGrid 上執行磁磚分割
        /// </summary>
        public void ExecuteOnGrid(CurtainGrid curtainGrid, Element hostElement)
        {
            BoundingBoxXYZ bbox = hostElement.get_BoundingBox(null);
            if (bbox == null)
                throw new InvalidOperationException("無法取得元素的邊界框。");

            XYZ minPt = bbox.Min;
            XYZ maxPt = bbox.Max;
            double lengthX = maxPt.X - minPt.X;
            double lengthY = maxPt.Y - minPt.Y;
            double lengthZ = maxPt.Z - minPt.Z;

            bool isHorizontal = (lengthZ < Math.Min(lengthX, lengthY) * 0.5);

            if (isHorizontal)
            {
                ExecuteOnHorizontalSystem(curtainGrid, minPt, maxPt, lengthX, lengthY);
            }
            else
            {
                XYZ start, dir;
                double length, height, baseZ;

                if (lengthX >= lengthY)
                {
                    start = new XYZ(minPt.X, (minPt.Y + maxPt.Y) / 2.0, minPt.Z);
                    dir = XYZ.BasisX;
                    length = lengthX;
                }
                else
                {
                    start = new XYZ((minPt.X + maxPt.X) / 2.0, minPt.Y, minPt.Z);
                    dir = XYZ.BasisY;
                    length = lengthY;
                }

                height = lengthZ;
                baseZ = minPt.Z;
                ExecuteVerticalSystem(curtainGrid, start, dir, length, baseZ, height);
            }
        }

        /// <summary>水平帷幕系統（屋頂）</summary>
        private void ExecuteOnHorizontalSystem(CurtainGrid curtainGrid,
            XYZ minPt, XYZ maxPt, double lengthX, double lengthY)
        {
            double midZ = (minPt.Z + maxPt.Z) / 2.0;

            // U grid lines (true) — 水平線分隔 Y
            int numU = (int)Math.Floor(lengthY / _config.CellHeightFeet);
            for (int i = 1; i <= numU; i++)
            {
                double yPos = minPt.Y + i * _config.CellHeightFeet;
                if (yPos >= maxPt.Y - 0.001) break;

                XYZ pt = new XYZ((minPt.X + maxPt.X) / 2.0, yPos, midZ);
                try { SafeAddGridLine(curtainGrid, true, pt); }
                catch (Exception) { }
            }

            if (_config.PatternType == TilePatternType.Grid)
            {
                // V grid lines (false) — 垂直線分隔 X
                int numV = (int)Math.Floor(lengthX / _config.CellWidthFeet);
                for (int i = 1; i <= numV; i++)
                {
                    double xPos = minPt.X + i * _config.CellWidthFeet;
                    if (xPos >= maxPt.X - 0.001) break;

                    XYZ pt = new XYZ(xPos, (minPt.Y + maxPt.Y) / 2.0, midZ);
                    try { SafeAddGridLine(curtainGrid, false, pt); }
                    catch (Exception) { }
                }
            }
            else
            {
                AddRunningBondHorizontal(curtainGrid, minPt, maxPt, lengthX, lengthY);
            }

            // [使用者要求] 暫時移除灰縫功能
            // AddMullionsAsGrout(curtainGrid);
        }

        /// <summary>垂直帷幕系統</summary>
        private void ExecuteVerticalSystem(CurtainGrid curtainGrid,
            XYZ start, XYZ dir, double length, double baseZ, double height)
        {
            double midZ = baseZ + height / 2.0;

            // U grid lines (true) = 水平
            int numH = (int)Math.Floor(height / _config.CellHeightFeet);
            for (int i = 1; i <= numH; i++)
            {
                double z = baseZ + i * _config.CellHeightFeet;
                if (z >= baseZ + height - 0.001) break;

                XYZ mid = start + dir * (length / 2.0);
                XYZ pt = new XYZ(mid.X, mid.Y, z);
                try { SafeAddGridLine(curtainGrid, true, pt); }
                catch (Exception) { }
            }

            if (_config.PatternType == TilePatternType.Grid)
            {
                // V grid lines (false) = 垂直
                int numV = (int)Math.Floor(length / _config.CellWidthFeet);
                for (int i = 1; i <= numV; i++)
                {
                    double dist = i * _config.CellWidthFeet;
                    if (dist >= length - 0.001) break;

                    XYZ pt = start + dir * dist;
                    pt = new XYZ(pt.X, pt.Y, midZ);
                    try { SafeAddGridLine(curtainGrid, false, pt); }
                    catch (Exception) { }
                }
            }
            else
            {
                AddRunningBondVerticals(curtainGrid, start, dir, length, baseZ, height, midZ);
            }

            // [使用者要求] 暫時移除灰縫功能
            // AddMullionsAsGrout(curtainGrid);
        }

        /// <summary>
        /// 自動為因連動平移而產生缺口的邊緣補上格線
        /// </summary>
        public void FillMissingGridLines(Element hostElement, bool isU)
        {
            if (!(hostElement is Wall wall)) return;

            CurtainGrid curtainGrid = wall.CurtainGrid;
            if (curtainGrid == null) return;
            
            LocationCurve locCurve = wall.Location as LocationCurve;
            if (locCurve == null) return;
            
            Curve wallCurve = locCurve.Curve;
            XYZ wallStart = wallCurve.GetEndPoint(0);
            XYZ wallEnd = wallCurve.GetEndPoint(1);
            XYZ wallDir = (wallEnd - wallStart).Normalize();
            double wallLength = wallCurve.Length;
            double wallHeight = GetWallHeight(wall);
            double baseZ = GetWallBaseElevation(wall);

            if (isU)
            {
                var uIds = curtainGrid.GetUGridLineIds();
                if (uIds.Count == 0) return;

                List<double> existingZ = new List<double>();
                foreach (var id in uIds)
                {
                    var gl = _doc.GetElement(id) as CurtainGridLine;
                    if (gl != null) existingZ.Add(GetGridLinePoint(gl).Z);
                }

                double refZ = existingZ.First();
                double cellH = _config.CellHeightFeet;
                XYZ hMidPoint = wallCurve.Evaluate(0.5, true);

                int minK = (int)Math.Floor((baseZ - refZ) / cellH) - 1;
                int maxK = (int)Math.Ceiling(((baseZ + wallHeight) - refZ) / cellH) + 1;

                for (int k = minK; k <= maxK; k++)
                {
                    double z = refZ + k * cellH;
                    if (z > baseZ + 0.001 && z < baseZ + wallHeight - 0.001)
                    {
                        if (!existingZ.Any(ez => Math.Abs(ez - z) < 0.01))
                        {
                            XYZ pt = new XYZ(hMidPoint.X, hMidPoint.Y, z);
                            try { SafeAddGridLine(curtainGrid, true, pt); }
                            catch { }
                        }
                    }
                }
            }
            else
            {
                var vIds = curtainGrid.GetVGridLineIds();
                if (vIds.Count == 0) return;

                List<double> existingDist = new List<double>();
                foreach (var id in vIds)
                {
                    var gl = _doc.GetElement(id) as CurtainGridLine;
                    if (gl != null)
                    {
                        XYZ pt = GetGridLinePoint(gl);
                        double dist = (pt - wallStart).DotProduct(wallDir);
                        existingDist.Add(Math.Round(dist, 4));
                    }
                }

                double cellW = _config.CellWidthFeet;
                double midZ = baseZ + wallHeight / 2.0;

                HashSet<double> modulos = new HashSet<double>(new DoubleComparer());
                foreach (double d in existingDist)
                {
                    double mod = d % cellW;
                    if (mod < 0) mod += cellW;
                    modulos.Add(mod);
                }

                foreach (double mod in modulos)
                {
                    int minK = (int)Math.Floor((0 - mod) / cellW) - 1;
                    int maxK = (int)Math.Ceiling((wallLength - mod) / cellW) + 1;

                    for (int k = minK; k <= maxK; k++)
                    {
                        double dist = mod + k * cellW;
                        if (dist > 0.001 && dist < wallLength - 0.001)
                        {
                            if (!existingDist.Any(ed => Math.Abs(ed - dist) < 0.01))
                            {
                                XYZ pt = wallStart + wallDir * dist;
                                pt = new XYZ(pt.X, pt.Y, midZ);
                                try { SafeAddGridLine(curtainGrid, false, pt); }
                                catch { }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>交丁排垂直格線</summary>
        private Tuple<int, int> AddRunningBondVerticals(CurtainGrid curtainGrid,
            XYZ wallStart, XYZ wallDir, double wallLength,
            double baseZ, double wallHeight, double midZ)
        {
            int count = 0, fail = 0;
            double offsetAmount = _config.CellWidthFeet * _config.RunningBondOffset;

            HashSet<double> allPositions = new HashSet<double>(new DoubleComparer());
            int numCols = (int)Math.Ceiling(wallLength / _config.CellWidthFeet) + 1;

            for (int i = 1; i <= numCols; i++)
            {
                double pos = i * _config.CellWidthFeet;
                if (pos > 0.001 && pos < wallLength - 0.001)
                    allPositions.Add(pos);
            }
            for (int i = 0; i <= numCols; i++)
            {
                double pos = i * _config.CellWidthFeet + offsetAmount;
                if (pos > 0.001 && pos < wallLength - 0.001)
                    allPositions.Add(pos);
            }

            var sorted = allPositions.ToList();
            sorted.Sort();

            foreach (double dist in sorted)
            {
                XYZ pt = wallStart + wallDir * dist;
                pt = new XYZ(pt.X, pt.Y, midZ);
                try
                {
                    SafeAddGridLine(curtainGrid, false, pt);
                    count++;
                }
                catch (Exception) { fail++; }
            }

            // 移除交丁排不需要的 segment
            _doc.Regenerate();
            RemoveExtraSegments(curtainGrid, wallStart, wallDir, baseZ, offsetAmount);

            return Tuple.Create(count, fail);
        }

        private void RemoveExtraSegments(CurtainGrid curtainGrid,
            XYZ wallStart, XYZ wallDir, double baseZ, double offsetAmount)
        {
            foreach (ElementId id in curtainGrid.GetVGridLineIds())
            {
                CurtainGridLine gl = _doc.GetElement(id) as CurtainGridLine;
                if (gl == null) continue;

                double dist = GetGridLineDistance(gl, wallStart, wallDir);
                bool isEven = IsOnGrid(dist, _config.CellWidthFeet);
                bool isOdd = IsOnGrid(dist - offsetAmount, _config.CellWidthFeet);

                if (isEven && !isOdd)
                    RemoveSegments(gl, baseZ, false, _config.CellHeightFeet);
                else if (!isEven && isOdd)
                    RemoveSegments(gl, baseZ, true, _config.CellHeightFeet);
            }
        }

        private void RemoveSegments(CurtainGridLine gl, double baseZ, bool removeEven, double cellHeight)
        {
            try
            {
                CurveArray segs = gl.AllSegmentCurves;
                // Revit 會自動合併相鄰的 segment，因此移除時要由上往下移，或是收集起來一次移
                // 在二丁掛的情境，這段迴圈會變得極度肥大 (幾百條線 * 幾百個 segment = 幾萬次操作)
                var toRemove = new List<Curve>();

                foreach (Curve seg in segs)
                {
                    double midZ = (seg.GetEndPoint(0).Z + seg.GetEndPoint(1).Z) / 2.0;
                    int row = (int)Math.Floor((midZ - baseZ) / cellHeight);
                    bool isEven = (row % 2 == 0);
                    if ((removeEven && isEven) || (!removeEven && !isEven))
                        toRemove.Add(seg);
                }

                if (toRemove.Count > 0)
                {
                    // 為了效能，避免在這裡一直抓錯
                    foreach (Curve seg in toRemove)
                    {
                        try { gl.RemoveSegment(seg); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>交丁排水平屋頂格線</summary>
        private void AddRunningBondHorizontal(CurtainGrid curtainGrid,
            XYZ minPt, XYZ maxPt, double lengthX, double lengthY)
        {
            double offsetAmount = _config.CellWidthFeet * _config.RunningBondOffset;
            double midZ = (minPt.Z + maxPt.Z) / 2.0;

            HashSet<double> allPositions = new HashSet<double>(new DoubleComparer());
            int numCols = (int)Math.Ceiling(lengthX / _config.CellWidthFeet) + 1;

            for (int i = 1; i <= numCols; i++)
            {
                double pos = i * _config.CellWidthFeet;
                if (pos > 0.001 && pos < lengthX - 0.001)
                    allPositions.Add(pos);
            }
            for (int i = 0; i <= numCols; i++)
            {
                double pos = i * _config.CellWidthFeet + offsetAmount;
                if (pos > 0.001 && pos < lengthX - 0.001)
                    allPositions.Add(pos);
            }

            var sorted = allPositions.ToList();
            sorted.Sort();

            foreach (double dist in sorted)
            {
                double xPos = minPt.X + dist;
                XYZ pt = new XYZ(xPos, (minPt.Y + maxPt.Y) / 2.0, midZ);
                try { SafeAddGridLine(curtainGrid, false, pt); }
                catch (Exception) { }
            }

            // 移除交丁排不需要的 segment
            _doc.Regenerate();
            foreach (ElementId id in curtainGrid.GetVGridLineIds())
            {
                CurtainGridLine gl = _doc.GetElement(id) as CurtainGridLine;
                if (gl == null) continue;

                XYZ mid = GetGridLinePoint(gl);
                double dist = mid.X - minPt.X;

                bool isEven = IsOnGrid(dist, _config.CellWidthFeet);
                bool isOdd = IsOnGrid(dist - offsetAmount, _config.CellWidthFeet);

                if (isEven && !isOdd)
                    RemoveSegmentsHorizontal(gl, minPt.Y, false, _config.CellHeightFeet);
                else if (!isEven && isOdd)
                    RemoveSegmentsHorizontal(gl, minPt.Y, true, _config.CellHeightFeet);
            }
        }

        private XYZ GetGridLinePoint(CurtainGridLine gl)
        {
            try { return gl.FullCurve.Evaluate(0.5, true); }
            catch { return XYZ.Zero; }
        }

        private void RemoveSegmentsHorizontal(CurtainGridLine gl, double baseY, bool removeEven, double cellHeight)
        {
            try
            {
                CurveArray segs = gl.AllSegmentCurves;
                var toRemove = new List<Curve>();

                foreach (Curve seg in segs)
                {
                    double midY = (seg.GetEndPoint(0).Y + seg.GetEndPoint(1).Y) / 2.0;
                    int row = (int)Math.Floor((midY - baseY) / cellHeight);
                    bool isEven = (row % 2 == 0);
                    if ((removeEven && isEven) || (!removeEven && !isEven))
                        toRemove.Add(seg);
                }

                if (toRemove.Count > 0)
                {
                    foreach (Curve seg in toRemove)
                    {
                        try { gl.RemoveSegment(seg); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        private void AddMullionsAsGrout(CurtainGrid curtainGrid)
        {
            MullionType groutType = GetOrCreateGroutMullionType();
            if (groutType == null) return;

            _doc.Regenerate();

            // 針對二丁掛等大量格線的情況，不逐一加上全段格線，那會造成幾萬次例外和重新計算
            // Revit 有提供整片加入或按線段加入，此處我們針對有實際 Segment 的地方加入即可
            
            // 由於 AddMullions 會自動忽略已存在的部分，我們可以直接對整個 Grid 工作，但在效能上
            // 避免對被 RemoveSegment 刪掉的部分呼叫 AddMullions。
            ProcessGridLinesForMullions(curtainGrid.GetUGridLineIds(), groutType);
            ProcessGridLinesForMullions(curtainGrid.GetVGridLineIds(), groutType);
        }

        private void ProcessGridLinesForMullions(ICollection<ElementId> glIds, MullionType groutType)
        {
            foreach (ElementId id in glIds)
            {
                CurtainGridLine gl = _doc.GetElement(id) as CurtainGridLine;
                if (gl == null) continue;
                
                CurveArray segments = gl.AllSegmentCurves;
                foreach (Curve seg in segments)
                {
                    try { gl.AddMullions(seg, groutType, false); }
                    catch (Exception) { }
                }
            }
        }

        private MullionType GetOrCreateGroutMullionType()
        {
            string typeName = $"灰縫_{_config.GroutWidth}mm";

            MullionType existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(MullionType))
                .Cast<MullionType>()
                .FirstOrDefault(mt => mt.Name == typeName);

            if (existing != null)
            {
                SetMullionDimensions(existing);
                return existing;
            }

            MullionType baseType = new FilteredElementCollector(_doc)
                .OfClass(typeof(MullionType))
                .Cast<MullionType>()
                .FirstOrDefault(mt => mt.FamilyName.Contains("矩形") ||
                                      mt.FamilyName.Contains("Rectangular") ||
                                      mt.FamilyName.Contains("rectangular"));

            if (baseType == null)
            {
                baseType = new FilteredElementCollector(_doc)
                    .OfClass(typeof(MullionType))
                    .Cast<MullionType>()
                    .FirstOrDefault();
            }

            if (baseType == null) return null;

            MullionType newType = baseType.Duplicate(typeName) as MullionType;
            if (newType != null) SetMullionDimensions(newType);
            return newType;
        }

        private void SetMullionDimensions(MullionType mt)
        {
            try
            {
                double hw = _config.GroutWidthFeet / 2.0;
                Parameter w1 = mt.get_Parameter(BuiltInParameter.RECT_MULLION_WIDTH1);
                if (w1 != null && !w1.IsReadOnly) w1.Set(hw);
                Parameter w2 = mt.get_Parameter(BuiltInParameter.RECT_MULLION_WIDTH2);
                if (w2 != null && !w2.IsReadOnly) w2.Set(hw);
            }
            catch (Exception) { }

            try
            {
                Parameter t = mt.get_Parameter(BuiltInParameter.RECT_MULLION_THICK);
                if (t != null && !t.IsReadOnly) t.Set(_config.GroutThicknessFeet);
            }
            catch (Exception) { }
        }

        // ===== 輔助 =====

        private double GetWallHeight(Wall wall)
        {
            Parameter p = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            return p != null ? p.AsDouble() : 0;
        }

        private double GetWallBaseElevation(Wall wall)
        {
            double baseOffset = 0;
            Parameter bop = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            if (bop != null) baseOffset = bop.AsDouble();

            ElementId levelId = wall.LevelId;
            if (levelId != ElementId.InvalidElementId)
            {
                Level level = _doc.GetElement(levelId) as Level;
                if (level != null) return level.Elevation + baseOffset;
            }
            return baseOffset;
        }

        private double GetGridLineDistance(CurtainGridLine gl, XYZ wallStart, XYZ wallDir)
        {
            try
            {
                XYZ mid = gl.FullCurve.Evaluate(0.5, true);
                return (mid - wallStart).DotProduct(wallDir);
            }
            catch { return 0; }
        }

        private bool IsOnGrid(double pos, double cell)
        {
            if (pos < -0.001) return false;
            double r = pos / cell;
            return Math.Abs(r - Math.Round(r)) < 0.01;
        }

        private static double FeetToMm(double feet) => feet * 304.8;

        private class DoubleComparer : IEqualityComparer<double>
        {
            public bool Equals(double x, double y) => Math.Abs(x - y) < 0.0001;
            public int GetHashCode(double obj) => Math.Round(obj, 3).GetHashCode();
        }
    }
}
