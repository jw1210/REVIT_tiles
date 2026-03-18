using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.Attributes;

namespace TilePlanner.Commands
{
    // 靜默處理器：自動核准系統層級的刪除錯誤與警告
    public class AutoDeleteFailureHandler : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            if (failures.Count == 0) return FailureProcessingResult.Continue;

            bool isResolved = false;
            foreach (FailureMessageAccessor failure in failures)
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                    isResolved = true;
                }
                else if (failure.GetSeverity() == FailureSeverity.Error && failure.HasResolutions())
                {
                    failuresAccessor.ResolveFailure(failure);
                    isResolved = true;
                }
            }
            return isResolved ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class AutoMiterJoinCommandV4115 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (!TilePlanner.Security.LicenseManager.Validate()) return Result.Failed;

            using (TransactionGroup tg = new TransactionGroup(doc, "雙側切角 V4.1.15.5"))
            {
                tg.Start();

                try
                {
                    double finalGapMm = 3.5;
                    double offsetDistanceFeet = (finalGapMm / 2.0) / 304.8; 

                    // 1. 選取流程
                    IList<Reference> refsA = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), "選取 A 側磁磚 (按 Enter 結束)");
                    if (refsA == null || refsA.Count == 0) return Result.Cancelled;

                    IList<Reference> refsB = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), "選取 B 側磁磚 (按 Enter 結束)");
                    if (refsB == null || refsB.Count == 0) return Result.Cancelled;

                    // 2. 幾何延伸與壁體處理 (V4.1.15.5 重點：強制延伸以確保重疊)
                    PrepareAndExtendParts(doc, refsA.Concat(refsB).ToList());

                    Part basePartA = doc.GetElement(refsA.First()) as Part;
                    Part basePartB = doc.GetElement(refsB.First()) as Part;

                    XYZ nA = GetDominantFaceNormal(basePartA);
                    XYZ nB = GetDominantFaceNormal(basePartB);

                    if (nA.CrossProduct(nB).GetLength() < 0.1)
                    {
                        TaskDialog.Show("幾何限制", "選取的兩側磁磚平行。");
                        return Result.Failed;
                    }

                    // 3. 計算核心幾何 (向量相加法)
                    GetTrueDiagonalByVectorAddition(basePartA, basePartB, nA, nB, out XYZ cutOrigin, out XYZ cornerDir, out XYZ bisectNormal);

                    List<ElementId> failedParts = new List<ElementId>();
                    
                    // 4. 執行隔離切割
                    var allRefs = refsA.Concat(refsB).ToList();
                    foreach (Reference r in allRefs)
                    {
                        if (!ProcessSinglePart(doc, r.ElementId, cutOrigin, cornerDir, bisectNormal, offsetDistanceFeet)) failedParts.Add(r.ElementId);
                    }

                    tg.Assimilate();

                    if (failedParts.Count > 0)
                    {
                        TaskDialog.Show("切角完成", $"處理完畢，但有 {failedParts.Count} 個零件處理失敗。");
                    }
                    else
                    {
                        TaskDialog.Show("TilePlanner", "雙側切角處理完成。");
                    }

                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    tg.RollBack();
                    TaskDialog.Show("系統例外", $"執行失敗：\n{ex.Message}");
                    return Result.Failed;
                }
            }
        }

        private void PrepareAndExtendParts(Document doc, List<Reference> refs)
        {
            using (Transaction t = new Transaction(doc, "牆體延伸處理"))
            {
                t.Start();
                foreach (Reference r in refs)
                {
                    Part p = doc.GetElement(r) as Part;
                    if (p == null) continue;
                    
                    var sourceIds = p.GetSourceElementIds();
                    foreach (LinkElementId lid in sourceIds)
                    {
                        Wall wall = doc.GetElement(lid.HostElementId) as Wall;
                        if (wall != null)
                        {
                            // 清除接合以便零件能「完整」伸展到角點
                            if (WallUtils.IsWallJoinAllowedAtEnd(wall, 0)) WallUtils.DisallowWallJoinAtEnd(wall, 0);
                            if (WallUtils.IsWallJoinAllowedAtEnd(wall, 1)) WallUtils.DisallowWallJoinAtEnd(wall, 1);
                        }
                    }
                }
                doc.Regenerate(); // 確保 Part 幾何隨牆體解鎖而伸展開來
                t.Commit();
            }
        }

        private bool ProcessSinglePart(Document doc, ElementId partId, XYZ cutOrigin, XYZ cornerDir, XYZ bisectNormal, double offset)
        {
            using (Transaction trans = new Transaction(doc, $"Miter Cut {partId.IntegerValue}"))
            {
                trans.Start();
                trans.SetFailureHandlingOptions(trans.GetFailureHandlingOptions().SetFailuresPreprocessor(new AutoDeleteFailureHandler()));

                try
                {
                    Part p = doc.GetElement(partId) as Part;
                    if (p == null) return false;

                    XYZ centroid = GetCentroid(p);
                    XYZ vecToCentroid = (centroid - cutOrigin).Normalize();
                    
                    // 向量加法平分線：nA+nB 得到的方向 朝向「角點外部」
                    // 這裡判断重心相對於切線的方向，決定 inwardDir
                    XYZ inwardDir = (bisectNormal.DotProduct(vecToCentroid) > 0) ? bisectNormal : -bisectNormal;

                    XYZ finalCutOrigin = cutOrigin + inwardDir * offset;

                    Plane cutPlane = Plane.CreateByNormalAndOrigin(inwardDir, finalCutOrigin);
                    SketchPlane sp = SketchPlane.Create(doc, cutPlane);
                    
                    // 繪製強效切割線 (確保完全穿透延伸後的 Part)
                    Line cutLine = Line.CreateBound(finalCutOrigin - cornerDir * 1000.0, finalCutOrigin + cornerDir * 1000.0);
                    
                    PartUtils.DivideParts(doc, new List<ElementId> { partId }, new List<ElementId>(), new List<Curve> { cutLine }, sp.Id);
                    doc.Regenerate();

                    // 廢料判定 (重心 vs 切割面)
                    ICollection<ElementId> subParts = PartUtils.GetAssociatedParts(doc, partId, false, true);
                    if (subParts.Count > 1)
                    {
                        foreach (ElementId subId in subParts)
                        {
                            Part sub = doc.GetElement(subId) as Part;
                            if (sub == null) continue;

                            XYZ subCentroid = GetCentroid(sub);
                            XYZ vecToSub = (subCentroid - finalCutOrigin);
                            
                            // 判定是否位於切割方向背後 (廢料側)
                            if (vecToSub.DotProduct(inwardDir) < -1e-6)
                            {
                                doc.Delete(subId);
                            }
                        }
                    }

                    trans.Commit();
                    return true;
                }
                catch { trans.RollBack(); return false; }
            }
        }

        private void GetTrueDiagonalByVectorAddition(Part partA, Part partB, XYZ nA, XYZ nB, out XYZ cutOrigin, out XYZ cornerDir, out XYZ bisectNormal)
        {
            Solid sA = GetSolid(partA);
            Solid sB = GetSolid(partB);
            
            var facesA = GetMainFaces(sA, nA);
            var facesB = GetMainFaces(sB, nB);

            if (facesA.Count < 2 || facesB.Count < 2) throw new InvalidOperationException("幾何層次不足。");

            // [V4.1.15.5] 向量相加法：對應 45度轉角向量
            bisectNormal = (nA + nB).Normalize();
            cornerDir = XYZ.BasisZ;

            // 依舊使用 3D 交點計算以定位角點座標
            cutOrigin = Solve3PlaneIntersection(nA, facesA[0].Origin, nB, facesB[0].Origin, cornerDir, facesA[0].Origin);
            
            if (cutOrigin == null) throw new InvalidOperationException("找不到幾何交點。");
        }

        private XYZ Solve3PlaneIntersection(XYZ n1, XYZ p1, XYZ n2, XYZ p2, XYZ n3, XYZ p3)
        {
            double d1 = n1.DotProduct(p1);
            double d2 = n2.DotProduct(p2);
            double d3 = n3.DotProduct(p3);

            double det = n1.X * (n2.Y * n3.Z - n2.Z * n3.Y) - n1.Y * (n2.X * n3.Z - n2.Z * n3.X) + n1.Z * (n2.X * n3.Y - n2.Y * n3.X);
            if (Math.Abs(det) < 1e-9) return null;

            double x = (d1 * (n2.Y * n3.Z - n2.Z * n3.Y) - n1.Y * (d2 * n3.Z - n2.Z * d3) + n1.Z * (d2 * n3.Y - n2.Y * d3)) / det;
            double y = (n1.X * (d2 * n3.Z - n2.Z * d3) - d1 * (n2.X * n3.Z - n2.Z * n3.X) + n1.Z * (n2.X * d3 - d2 * n3.X)) / det;
            double z = (n1.X * (n2.Y * d3 - d2 * n3.Y) - n1.Y * (n2.X * d3 - d2 * n3.X) + d1 * (n2.X * n3.Y - n2.Y * n3.X)) / det;

            return new XYZ(x, y, z);
        }

        private List<PlanarFace> GetMainFaces(Solid s, XYZ normal)
        {
            return s.Faces.OfType<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.DotProduct(normal)) > 0.999)
                .OrderByDescending(f => f.Area).Take(2).ToList();
        }

        private XYZ GetDominantFaceNormal(Part p)
        {
            Solid s = GetSolid(p);
            return s.Faces.OfType<PlanarFace>().OrderByDescending(f => f.Area).First().FaceNormal;
        }

        private Solid GetSolid(Part p) => p.get_Geometry(new Options { ComputeReferences = true }).OfType<Solid>().FirstOrDefault(s => s.Volume > 0);

        private XYZ GetCentroid(Element e) 
        { 
            BoundingBoxXYZ b = e.get_BoundingBox(null); 
            return b != null ? (b.Min + b.Max) / 2.0 : XYZ.Zero; 
        }
    }

    public class PartOnlyFilter : ISelectionFilter 
    {
        public bool AllowElement(Element e) => e is Part;
        public bool AllowReference(Reference r, XYZ p) => true;
    }
}
