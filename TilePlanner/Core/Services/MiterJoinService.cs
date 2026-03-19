using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TilePlanner.Core.Utils;

namespace TilePlanner.Core.Services
{
    /// <summary>
    /// 斜切接合引擎中樞。負責協調準備階段(Phase Prep)與分側處理(Phase A/B)
    /// </summary>
    public static class MiterJoinService
    {
        public static Result ExecuteMiterJoin(Document doc, IList<Reference> refsA, IList<Reference> refsB)
        {
            using (TransactionGroup tg = new TransactionGroup(doc, "雙側切角 V4.1.21 (Modular)"))
            {
                tg.Start();
                try
                {
                    double offsetFeet = 0.0; // Zero Gap 邏輯

                    // 1. 幾何收集與排序
                    var partsA = refsA.Select(r => doc.GetElement(r) as Part).Where(p => p != null).OrderBy(p => p.GetCentroid().Z).ToList();
                    var partsB = refsB.Select(r => doc.GetElement(r) as Part).Where(p => p != null).OrderBy(p => p.GetCentroid().Z).ToList();

                    // 2. 獲取主體牆與計算交點
                    Wall wallA = partsA.First().GetHostWall();
                    Wall wallB = partsB.First().GetHostWall();
                    if (wallA == null || wallB == null) throw new Exception("無法定位主體牆。");

                    Curve lineA = (wallA.Location as LocationCurve)?.Curve;
                    Curve lineB = (wallB.Location as LocationCurve)?.Curve;
                    if (lineA == null || lineB == null) throw new Exception("牆體中心線異常。");

                    IntersectionResultArray ira;
                    if (lineA.Intersect(lineB, out ira) != SetComparisonResult.Overlap)
                    {
                        TaskDialog.Show("警告", "牆體中心線未相交。");
                        return Result.Failed;
                    }
                    XYZ intersectXY = ira.get_Item(0).XYZPoint;

                    // -------------------------------------------------------------------------
                    // 準備階段：解開接合併延伸牆體 (Flush to Outer face)
                    // -------------------------------------------------------------------------
                    using (Transaction tPrep = new Transaction(doc, "延伸牆體"))
                    {
                        tPrep.Start();
                        WallGeometryService.ExtendWallToIncludeCorner(wallA, intersectXY, wallB.Width);
                        WallGeometryService.ExtendWallToIncludeCorner(wallB, intersectXY, wallA.Width);
                        doc.Regenerate();
                        tPrep.Commit();
                    }

                    // 3. 計算 Miter 基準 (nA - nB)
                    XYZ nA_ref = partsA.First().GetDominantFaceNormal();
                    XYZ nB_ref = partsB.First().GetDominantFaceNormal();
                    XYZ miterNormal = (nA_ref - nB_ref).Normalize();

                    // -------------------------------------------------------------------------
                    // 分側序列執行 (Phase A -> Phase B)
                    // -------------------------------------------------------------------------
                    
                    // Phase A
                    int successA = 0;
                    using (Transaction tA = new Transaction(doc, "Phase A Split"))
                    {
                        tA.Start();
                        tA.SetFailureHandlingOptions(tA.GetFailureHandlingOptions().SetFailuresPreprocessor(new AutoDeleteFailureHandler()));
                        foreach (Part p in partsA)
                        {
                            XYZ origin = new XYZ(intersectXY.X, intersectXY.Y, p.GetCentroid().Z);
                            if (PartOperationService.CutAndExcludeWasteZeroGap(doc, p.Id, origin, miterNormal, offsetFeet)) successA++;
                        }
                        doc.Regenerate();
                        tA.Commit();
                    }

                    // Phase B
                    int successB = 0;
                    using (Transaction tB = new Transaction(doc, "Phase B Split"))
                    {
                        tB.Start();
                        tB.SetFailureHandlingOptions(tB.GetFailureHandlingOptions().SetFailuresPreprocessor(new AutoDeleteFailureHandler()));
                        foreach (Part p in partsB)
                        {
                            XYZ origin = new XYZ(intersectXY.X, intersectXY.Y, p.GetCentroid().Z);
                            if (PartOperationService.CutAndExcludeWasteZeroGap(doc, p.Id, origin, miterNormal, offsetFeet)) successB++;
                        }
                        doc.Regenerate();
                        tB.Commit();
                    }

                    tg.Assimilate(); // 確認所有操作
                    TaskDialog.Show("TilePlanner V4.1.21", $"模組化重構完成！\nA 成功：{successA}\nB 成功：{successB}\n(已套用對位延伸與斜切修正)");
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    tg.RollBack();
                    TaskDialog.Show("錯誤", $"執行失敗：{ex.Message}");
                    return Result.Failed;
                }
            }
        }
    }
}
