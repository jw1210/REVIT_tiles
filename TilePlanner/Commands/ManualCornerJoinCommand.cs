using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.Attributes;

namespace TilePlanner.Commands
{
    // --- 靜默處理器：自動核准系統層級之幾何刪除警告 ---
    public class SilentFailureHandler : IFailuresPreprocessor
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
    public class ManualCornerJoinCommand : IExternalCommand
    {
        private enum JoinMode { Miter, Butt, Cancel }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (!TilePlanner.Security.LicenseManager.Validate()) return Result.Failed;

            try
            {
                TaskDialog.Show("版本驗證", "TilePlanner V4.1.12 核心模組啟動。");

                JoinMode mode = GetJoinModeFromUser();
                if (mode == JoinMode.Cancel) return Result.Cancelled;

                // 參數設定：強制最低灰縫間距為 3.5mm
                double userInputGapMm = 3.5; 
                double finalGapMm = Math.Max(3.5, userInputGapMm);
                double offsetDistanceFeet = (finalGapMm / 2.0) / 304.8; 

                // 選取流程
                IList<Reference> refsA = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), "選取 A 側零件群 (按 Enter 結束)");
                if (refsA.Count == 0) return Result.Cancelled;

                IList<Reference> refsB = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), "選取 B 側零件群 (按 Enter 結束)");
                if (refsB.Count == 0) return Result.Cancelled;

                using (TransactionGroup tg = new TransactionGroup(doc, "磁磚計畫 - V4.1.12 雙側切角"))
                {
                    tg.Start();

                    XYZ nA, nB, cornerOrigin;
                    double thicknessA, thicknessB;
                    Dictionary<ElementId, Tuple<XYZ, XYZ>> cutDataDict = new Dictionary<ElementId, Tuple<XYZ, XYZ>>();

                    // ==========================================
                    // 第一階段：幾何偵測、厚度計算與強制延伸
                    // ==========================================
                    using (Transaction trans1 = new Transaction(doc, "階段一：幾何延伸與偵測"))
                    {
                        trans1.Start();
                        ApplySilentHandler(trans1);

                        Part basePartA = doc.GetElement(refsA.First()) as Part;
                        Part basePartB = doc.GetElement(refsB.First()) as Part;

                        // 1. 自動偵測外側垂直面法向量與 2D 外角交點
                        DetectOuterFaces(basePartA, basePartB, out nA, out nB, out cornerOrigin);

                        // 2. 自動偵測零件厚度
                        thicknessA = GetPartThickness(basePartA, nA);
                        thicknessB = GetPartThickness(basePartB, nB);

                        // 3. 強制執行端面延伸建立交疊幾何
                        foreach (Reference r in refsA) ExtendPartEndFace(doc, doc.GetElement(r) as Part, nA, cornerOrigin);
                        foreach (Reference r in refsB) ExtendPartEndFace(doc, doc.GetElement(r) as Part, nB, cornerOrigin);

                        trans1.Commit(); 
                    }

                    // ==========================================
                    // 第二階段：計算厚度加權對角線與參考平面軸
                    // ==========================================
                    // 厚度加權向量公式：計算真實內外交點對角線
                    XYZ cutDir = (thicknessA * nA + thicknessB * nB).Normalize();
                    XYZ shiftAxis = XYZ.BasisZ.CrossProduct(cutDir).Normalize();

                    // ==========================================
                    // 第三階段：位移判定與執行分割切割
                    // ==========================================
                    using (Transaction trans2 = new Transaction(doc, "階段二：分割切割"))
                    {
                        trans2.Start();
                        ApplySilentHandler(trans2);

                        // 處理 A 側切割原點與分割
                        foreach (Reference r in refsA)
                        {
                            Part p = doc.GetElement(r) as Part;
                            // 核心修正：直接傳入該側的外表面法向量 (nA) 進行絕對方向判定
                            XYZ inwardDir = CalculateInwardDirection(nA, shiftAxis);
                            
                            XYZ pCentroid = GetCentroid(p); 
                            XYZ specificCutOrigin = new XYZ(
                                cornerOrigin.X + inwardDir.X * offsetDistanceFeet,
                                cornerOrigin.Y + inwardDir.Y * offsetDistanceFeet,
                                pCentroid.Z
                            );
                            
                            cutDataDict[p.Id] = new Tuple<XYZ, XYZ>(specificCutOrigin, inwardDir);
                            ExecutePlanViewCut(doc, p.Id, specificCutOrigin, cutDir);
                        }

                        // 處理 B 側切割原點與分割
                        foreach (Reference r in refsB)
                        {
                            Part p = doc.GetElement(r) as Part;
                            // 核心修正：直接傳入該側的外表面法向量 (nB) 進行絕對方向判定
                            XYZ inwardDir = CalculateInwardDirection(nB, shiftAxis);
                            
                            XYZ pCentroid = GetCentroid(p); 
                            XYZ specificCutOrigin = new XYZ(
                                cornerOrigin.X + inwardDir.X * offsetDistanceFeet,
                                cornerOrigin.Y + inwardDir.Y * offsetDistanceFeet,
                                pCentroid.Z
                            );
                            
                            cutDataDict[p.Id] = new Tuple<XYZ, XYZ>(specificCutOrigin, inwardDir);
                            ExecutePlanViewCut(doc, p.Id, specificCutOrigin, cutDir);
                        }

                        if (trans2.Commit() != TransactionStatus.Committed)
                        {
                            throw new InvalidOperationException("Revit 拒絕執行切割作業。這表示切割平面未能與實體產生有效交集。");
                        }
                    }

                    // ==========================================
                    // 第四階段：半空間判定與廢料刪除
                    // ==========================================
                    using (Transaction trans3 = new Transaction(doc, "階段三：廢料刪除"))
                    {
                        trans3.Start();
                        ApplySilentHandler(trans3);

                        foreach (var kvp in cutDataDict)
                        {
                            ElementId originalPartId = kvp.Key;
                            XYZ specificCutOrigin = kvp.Value.Item1;
                            XYZ inwardDir = kvp.Value.Item2; 

                            DeleteWasteParts(doc, originalPartId, specificCutOrigin, inwardDir);
                        }

                        trans3.Commit(); 
                    }

                    tg.Assimilate(); 
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                TaskDialog.Show("系統例外", ex.Message);
                return Result.Failed;
            }
        }

        private void ApplySilentHandler(Transaction trans)
        {
            FailureHandlingOptions options = trans.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new SilentFailureHandler());
            options.SetClearAfterRollback(true);
            trans.SetFailureHandlingOptions(options);
        }

        private double GetPartThickness(Part part, XYZ outerNormal)
        {
            Solid s = GetSolid(part);
            if (s == null) return 0.05; 

            PlanarFace outerFace = s.Faces.OfType<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 0.01 && f.FaceNormal.DotProduct(outerNormal) > 0.98)
                .OrderByDescending(f => f.Area).FirstOrDefault();

            PlanarFace innerFace = s.Faces.OfType<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 0.01 && f.FaceNormal.DotProduct(outerNormal) < -0.98)
                .OrderByDescending(f => f.Area).FirstOrDefault();

            if (outerFace != null && innerFace != null)
            {
                return Math.Abs((outerFace.Origin - innerFace.Origin).DotProduct(outerNormal));
            }
            return 0.05; 
        }

        private void ExtendPartEndFace(Document doc, Part part, XYZ outerNormal, XYZ cornerOrigin)
        {
            Solid s = GetSolid(part);
            if (s == null) return;

            PlanarFace endFace = s.Faces.OfType<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 0.01)
                .Where(f => Math.Abs(f.FaceNormal.DotProduct(outerNormal)) < 0.05) 
                .Where(f => f.Reference != null)
                .OrderBy(f => f.Origin.DistanceTo(cornerOrigin))
                .FirstOrDefault();

            if (endFace != null)
            {
                if (part.CanOffsetFace(endFace))
                {
                    part.SetFaceOffset(endFace, 100.0 / 304.8); 
                }
            }
        }

        private void DetectOuterFaces(Part partA, Part partB, out XYZ nA, out XYZ nB, out XYZ cornerOrigin)
        {
            var facesA = GetTopTwoVerticalFaces(partA);
            var facesB = GetTopTwoVerticalFaces(partB);

            if (facesA.Count < 1 || facesB.Count < 1) 
                throw new InvalidOperationException("無法取得有效垂直面。");

            XYZ centroidA = GetCentroid(partA);
            XYZ centroidB = GetCentroid(partB);
            XYZ midCentroid = (centroidA + centroidB) / 2.0;

            double maxDistance = -1;
            PlanarFace bestFaceA = null;
            PlanarFace bestFaceB = null;
            XYZ bestIntersection = XYZ.Zero;

            foreach (PlanarFace fA in facesA)
            {
                foreach (PlanarFace fB in facesB)
                {
                    XYZ intersection = Calculate2DIntersection(fA, fB);
                    if (intersection != null)
                    {
                        double dist = intersection.DistanceTo(new XYZ(midCentroid.X, midCentroid.Y, intersection.Z));
                        if (dist > maxDistance)
                        {
                            maxDistance = dist;
                            bestFaceA = fA;
                            bestFaceB = fB;
                            bestIntersection = intersection;
                        }
                    }
                }
            }

            if (bestFaceA == null || bestFaceB == null) throw new InvalidOperationException("無法計算交點。");

            nA = bestFaceA.FaceNormal.Normalize();
            nB = bestFaceB.FaceNormal.Normalize();
            cornerOrigin = new XYZ(bestIntersection.X, bestIntersection.Y, midCentroid.Z);
        }

        private List<PlanarFace> GetTopTwoVerticalFaces(Part part)
        {
            Solid s = GetSolid(part);
            if (s == null) return new List<PlanarFace>();

            return s.Faces.OfType<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 0.01)
                .OrderByDescending(f => f.Area)
                .Take(2)
                .ToList();
        }

        private XYZ Calculate2DIntersection(PlanarFace fA, PlanarFace fB)
        {
            XYZ nA = fA.FaceNormal;
            XYZ nB = fB.FaceNormal;
            XYZ oA = fA.Origin;
            XYZ oB = fB.Origin;

            double d1 = nA.X * oA.X + nA.Y * oA.Y;
            double d2 = nB.X * oB.X + nB.Y * oB.Y;
            double det = nA.X * nB.Y - nA.Y * nB.X;

            if (Math.Abs(det) > 1e-6)
            {
                double x = (d1 * nB.Y - d2 * nA.Y) / det;
                double y = (nA.X * d2 - nB.X * d1) / det;
                return new XYZ(x, y, 0);
            }
            return null;
        }

        // --- 核心修正：純法向量內積判定 ---
        private XYZ CalculateInwardDirection(XYZ outerNormal, XYZ shiftAxis)
        {
            // 退縮向量必須指向實體內部，因此它與朝外的外表面法向量內積必定小於 0
            return (shiftAxis.DotProduct(outerNormal) < 0) ? shiftAxis : -shiftAxis;
        }

        private void ExecutePlanViewCut(Document doc, ElementId partId, XYZ cutOrigin, XYZ cutDir)
        {
            Plane horizontalPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, cutOrigin);
            SketchPlane sp = SketchPlane.Create(doc, horizontalPlane);
            
            Line cutLine = Line.CreateBound(cutOrigin - cutDir * 500.0, cutOrigin + cutDir * 500.0);
            PartUtils.DivideParts(doc, new List<ElementId> { partId }, new List<ElementId>(), new List<Curve> { cutLine }, sp.Id);
        }

        private void DeleteWasteParts(Document doc, ElementId originalPartId, XYZ cutOrigin, XYZ inwardDir)
        {
            ICollection<ElementId> subParts = PartUtils.GetAssociatedParts(doc, originalPartId, false, true);
            
            if (subParts.Count <= 1) return; 

            foreach (ElementId subId in subParts)
            {
                Part subPart = doc.GetElement(subId) as Part;
                if (subPart == null) continue;

                XYZ subCentroid = GetCentroid(subPart);
                XYZ vecSub = new XYZ(subCentroid.X - cutOrigin.X, subCentroid.Y - cutOrigin.Y, 0).Normalize();

                if (vecSub.DotProduct(inwardDir) < 0)
                {
                    doc.Delete(subId);
                }
            }
        }

        private XYZ GetCentroid(Element e) 
        { 
            BoundingBoxXYZ b = e.get_BoundingBox(null); 
            return b != null ? (b.Min + b.Max) / 2.0 : XYZ.Zero; 
        }

        private Solid GetSolid(Part p) => p.get_Geometry(new Options() { ComputeReferences = true }).OfType<Solid>().FirstOrDefault(s => s.Volume > 0);

        private JoinMode GetJoinModeFromUser()
        {
            TaskDialog td = new TaskDialog("手動轉角接合 V4.1.12")
            {
                MainInstruction = "請選擇轉角接合模式",
                MainContent = "灰縫間距已強制下限為 3.5mm 以確保幾何生成穩定。",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "雙側切角 (Miter Join)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "蓋磚 (Butt Join)");

            TaskDialogResult result = td.Show();
            if (result == TaskDialogResult.CommandLink1) return JoinMode.Miter;
            if (result == TaskDialogResult.CommandLink2) return JoinMode.Butt;
            return JoinMode.Cancel;
        }
    }

    public class PartOnlyFilter : ISelectionFilter 
    {
        public bool AllowElement(Element e) => e is Part;
        public bool AllowReference(Reference r, XYZ p) => true;
    }
}
