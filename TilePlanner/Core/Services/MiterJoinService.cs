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
                    // 獲取表面、牆向並執行延伸 (步驟 1 & 2)
                    // -------------------------------------------------------------------------
                    PlanarFace outA = null, inA = null, outB = null, inB = null;
                    XYZ wallDirA = ((wallA.Location as LocationCurve).Curve as Line).Direction.Normalize();
                    XYZ wallDirB = ((wallB.Location as LocationCurve).Curve as Line).Direction.Normalize();

                    using (Transaction tPrep = new Transaction(doc, "磁磚零件對位延伸"))
                    {
                        tPrep.Start();
                        foreach (var p in partsA) PartOperationService.SetPartShapeModified(p, true);
                        foreach (var p in partsB) PartOperationService.SetPartShapeModified(p, true);
                        doc.Regenerate();

                        // 抓取延伸前的初始表面
                        outA = PartOperationService.GetOuterFace(partsA.First(), wallA);
                        outB = PartOperationService.GetOuterFace(partsB.First(), wallB);

                        if (outA != null && outB != null)
                        {
                            // A 延伸至 B 外皮 (使用法向量投影計算距離)
                            foreach (var p in partsA)
                            {
                                PlanarFace endA = PartOperationService.GetEndFace(p, wallDirA, intersectXY);
                                if (endA != null)
                                {
                                    double dist = Math.Abs(outB.FaceNormal.DotProduct(endA.Origin - outB.Origin));
                                    p.SetFaceOffset(endA, dist + 0.05); // 0.05 呎重疊確保覆蓋
                                }
                            }

                            // B 延伸至 A 外皮
                            foreach (var p in partsB)
                            {
                                PlanarFace endB = PartOperationService.GetEndFace(p, wallDirB, intersectXY);
                                if (endB != null)
                                {
                                    double dist = Math.Abs(outA.FaceNormal.DotProduct(endB.Origin - outA.Origin));
                                    p.SetFaceOffset(endB, dist + 0.05);
                                }
                            }
                        }
                        
                        doc.Regenerate();
                        tPrep.Commit();
                    }

                    // -------------------------------------------------------------------------
                    // 步驟 3：nA + nB 向量斜切 (Final Vector Cut)
                    // -------------------------------------------------------------------------
                    
                    // 1. 獲取最終表面 (延伸後刷新)
                    outA = PartOperationService.GetOuterFace(partsA.First(), wallA);
                    inA = PartOperationService.GetInnerFace(partsA.First(), wallA);
                    outB = PartOperationService.GetOuterFace(partsB.First(), wallB);
                    inB = PartOperationService.GetInnerFace(partsB.First(), wallB);

                    if (outA == null || inA == null || outB == null || inB == null)
                    {
                        TaskDialog.Show("錯誤", "延伸後無法定位表面，請檢查牆向。");
                        return Result.Failed;
                    }

                    // 2. 利用 nA + nB 決定向量與交點
                    XYZ ptOuter = PartOperationService.GetIntersection2D(outA.Origin, outA.FaceNormal, outB.Origin, outB.FaceNormal);
                    XYZ ptInner = PartOperationService.GetIntersection2D(inA.Origin, inA.FaceNormal, inB.Origin, inB.FaceNormal);

                    if (ptOuter == null || ptInner == null)
                    {
                        TaskDialog.Show("錯誤", "交點計算失敗。");
                        return Result.Failed;
                    }

                    XYZ vDiag = (outA.FaceNormal + outB.FaceNormal).Normalize();
                    if (vDiag.GetLength() < 0.1) vDiag = (ptOuter - ptInner).Normalize(); // 防呆備案

                    // -------------------------------------------------------------------------
                    // 分側序列執行 (Phase A -> Phase B)
                    // -------------------------------------------------------------------------
                    
                    int successA = 0;
                    using (Transaction tA = new Transaction(doc, "Phase A - True Miter"))
                    {
                        tA.Start();
                        tA.SetFailureHandlingOptions(tA.GetFailureHandlingOptions().SetFailuresPreprocessor(new AutoDeleteFailureHandler()));
                        if (PartOperationService.PerformTrueDiagonalCut(doc, partsA, ptInner, vDiag, wallA)) successA = partsA.Count;
                        doc.Regenerate();
                        tA.Commit();
                    }

                    int successB = 0;
                    using (Transaction tB = new Transaction(doc, "Phase B - True Miter"))
                    {
                        tB.Start();
                        tB.SetFailureHandlingOptions(tB.GetFailureHandlingOptions().SetFailuresPreprocessor(new AutoDeleteFailureHandler()));
                        if (PartOperationService.PerformTrueDiagonalCut(doc, partsB, ptInner, vDiag, wallB)) successB = partsB.Count;
                        doc.Regenerate();
                        tB.Commit();
                    }

                    tg.Assimilate();
                    TaskDialog.Show("TilePlanner V4.3.4", $"45度背斜完成！\nA 側：{successA} 件\nB 側：{successB} 件\n(已切換至 True Diagonal 精確算法)");
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
