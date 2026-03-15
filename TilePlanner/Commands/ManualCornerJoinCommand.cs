using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace TilePlanner.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ManualCornerJoinCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. 彈出 UI 讓使用者選擇收邊形式
                TaskDialog td = new TaskDialog("選擇收邊形式 (V3.8)");
                td.MainInstruction = "請選擇您想要的轉角收邊形式：";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "45度切角 (Miter)", "分兩次選取轉角兩側的磁磚，自動切分為45度角");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "蓋磚 / 平接 (Cover)", "先選取「保持完整側」的磚，再選「被切斷側」的磚");
                
                TaskDialogResult result = td.Show();
                if (result != TaskDialogResult.CommandLink1 && result != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;

                bool isMiter = (result == TaskDialogResult.CommandLink1);
                
                // 2. 設定多重選取的提示語 (明確標示 Enter 確認)
                string promptA = isMiter ? 
                    "第 1 步：請點選或框選【第一側】的一或多塊磁磚 (選取完成請按 Enter 鍵確認)" : 
                    "第 1 步：請點選或框選【要蓋人 (保持完整側)】的一或多塊磁磚 (選取完成請按 Enter 鍵確認)";
                    
                string promptB = isMiter ? 
                    "第 2 步：請點選或框選【第二側】的一或多塊磁磚 (選取完成請按 Enter 鍵確認)" : 
                    "第 2 步：請點選或框選【被切斷 (被覆蓋側)】的一或多塊磁磚 (選取完成請按 Enter 鍵確認)";

                // 3. 執行分邊多選 (支援拉框、點選、Ctrl加選)
                IList<Reference> refsA = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), promptA);
                if (refsA == null || refsA.Count == 0) return Result.Cancelled;

                IList<Reference> refsB = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), promptB);
                if (refsB == null || refsB.Count == 0) return Result.Cancelled;

                Part partA = doc.GetElement(refsA[0]) as Part;
                Part partB = doc.GetElement(refsB[0]) as Part;

                if (partA == null || partB == null) return Result.Cancelled;

                List<ElementId> allSelectedIds = refsA.Select(r => r.ElementId)
                                                      .Concat(refsB.Select(r => r.ElementId))
                                                      .Distinct()
                                                      .ToList();

                // ==========================================
                // [V3.8 核心修正] 使用 TransactionGroup 防止靜默崩潰
                // ==========================================
                using (TransactionGroup tg = new TransactionGroup(doc, "局部轉角接合"))
                {
                    tg.Start();

                    try
                    {
                        // --- 階段 A：數學幾何運算 (在合併前進行，以免遺失原始幾何) ---
                        Solid solidA = GetSolid(partA);
                        Solid solidB = GetSolid(partB);
                        var facesA = GetTwoLargestFaces(solidA);
                        var facesB = GetTwoLargestFaces(solidB);

                        if (facesA.Count < 2 || facesB.Count < 2) 
                            throw new Exception("無法解析磁磚的表面幾何。");

                        XYZ axis = null;
                        PlanarFace bestFa = null;
                        double maxCross = 0;

                        foreach (var fa in facesA)
                        {
                            foreach (var fb in facesB)
                            {
                                XYZ cross = fa.FaceNormal.CrossProduct(fb.FaceNormal);
                                double len = cross.GetLength();
                                if (len > maxCross)
                                {
                                    maxCross = len;
                                    axis = cross;
                                    bestFa = fa;
                                }
                            }
                        }

                        if (maxCross < 0.1 || axis == null) 
                            throw new Exception("選取的兩側磁磚未能形成明顯的轉角 (幾近平行)，無法接合。");

                        axis = axis.Normalize();

                        XYZ origin = (solidA.ComputeCentroid() + solidB.ComputeCentroid()) / 2.0;
                        Plane sketchPlane = Plane.CreateByNormalAndOrigin(axis, origin);

                        List<XYZ> corners2D = new List<XYZ>();
                        foreach (var fa in facesA)
                            foreach (var fb in facesB)
                            {
                                XYZ pt = IntersectThreePlanes(fa, fb, sketchPlane);
                                if (pt != null) corners2D.Add(pt);
                            }

                        if (corners2D.Count < 4) 
                            throw new Exception("幾何運算失敗，未能形成完美的轉角特徵點。");

                        XYZ cA = ProjectToPlane(solidA.ComputeCentroid(), sketchPlane);
                        XYZ cB = ProjectToPlane(solidB.ComputeCentroid(), sketchPlane);
                        XYZ M = (cA + cB) / 2.0;

                        // 找出內角點(I)與外角點(O)
                        double maxDist = -1;
                        XYZ P1 = null, P2 = null;
                        for (int i = 0; i < corners2D.Count; i++)
                        {
                            for (int j = i + 1; j < corners2D.Count; j++)
                            {
                                double d = corners2D[i].DistanceTo(corners2D[j]);
                                if (d > maxDist) { maxDist = d; P1 = corners2D[i]; P2 = corners2D[j]; }
                            }
                        }

                        XYZ I, O;
                        if (P1.DistanceTo(M) < P2.DistanceTo(M)) { I = P1; O = P2; }
                        else { I = P2; O = P1; }

                        List<Curve> cutCurves = new List<Curve>();
                        if (isMiter)
                        {
                            XYZ dir = (O - I).Normalize();
                            cutCurves.Add(Line.CreateBound(I - dir * 20, O + dir * 20)); 
                        }
                        else
                        {
                            XYZ cutDir = axis.CrossProduct(bestFa.FaceNormal).Normalize(); 
                            cutCurves.Add(Line.CreateBound(I - cutDir * 20, I + cutDir * 20));
                        }

                        // --- 階段 B：實體鎔鑄 (合併) ---
                        PartMaker pmMerge = null;
                        using (Transaction tMerge = new Transaction(doc, "合併磁磚"))
                        {
                            FailureHandlingOptions mergeOptions = tMerge.GetFailureHandlingOptions();
                            mergeOptions.SetFailuresPreprocessor(new LocalWarningSwallower());
                            tMerge.SetFailureHandlingOptions(mergeOptions);

                            tMerge.Start();
                            pmMerge = PartUtils.CreateMergedPart(doc, allSelectedIds);
                            tMerge.Commit();
                        }

                        if (pmMerge == null) throw new Exception("磁磚合併失敗。");

                        // --- 階段 C：重新切割 ---
                        using (Transaction tCut = new Transaction(doc, "轉角切割"))
                        {
                            FailureHandlingOptions cutOptions = tCut.GetFailureHandlingOptions();
                            cutOptions.SetFailuresPreprocessor(new LocalWarningSwallower());
                            tCut.SetFailureHandlingOptions(cutOptions);

                            tCut.Start();
                            doc.Regenerate(); // 確保合併結果已在系統註冊

                            ElementId newPartId = ElementId.InvalidElementId;
                            var allParts = new FilteredElementCollector(doc).OfClass(typeof(Part)).ToElements();
                            foreach (Part p in allParts)
                            {
                                var maker = PartUtils.GetAssociatedPartMaker(doc, p.Id);
                                if (maker != null && maker.Id == pmMerge.Id) 
                                { 
                                    newPartId = p.Id; break; 
                                }
                            }

                            if (newPartId == ElementId.InvalidElementId)
                                throw new Exception("無法尋找到合併後的新實體。");

                            SketchPlane sp = SketchPlane.Create(doc, sketchPlane);
                            PartUtils.DivideParts(doc, new List<ElementId> { newPartId }, new List<ElementId>(), cutCurves, sp.Id);
                            tCut.Commit();
                        }

                        tg.Assimilate(); // 成功完成，將兩次微交易融合
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        tg.RollBack();
                        // 【關鍵】如果失敗，絕對會彈出視窗，不再靜默無反應！
                        TaskDialog.Show("局部轉角 - 失敗", $"無法完成轉角接合。\n原因：{ex.Message}");
                        return Result.Failed;
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) 
            { 
                TaskDialog.Show("錯誤", ex.Message);
                return Result.Failed; 
            }
        }

        // --- 以下為幾何輔助工具 ---
        private Solid GetSolid(Part part)
        {
            Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geomElem = part.get_Geometry(opt);
            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 0) return solid;
            }
            return null;
        }

        private List<PlanarFace> GetTwoLargestFaces(Solid solid)
        {
            var faces = new List<PlanarFace>();
            foreach (Face f in solid.Faces)
            {
                if (f is PlanarFace pf) faces.Add(pf);
            }
            return faces.OrderByDescending(f => f.Area).Take(2).ToList();
        }

        private XYZ ProjectToPlane(XYZ point, Plane plane)
        {
            double distance = plane.Normal.DotProduct(point - plane.Origin);
            return point - distance * plane.Normal;
        }

        private XYZ IntersectThreePlanes(PlanarFace f1, PlanarFace f2, Plane p3)
        {
            XYZ n1 = f1.FaceNormal; double d1 = n1.DotProduct(f1.Origin);
            XYZ n2 = f2.FaceNormal; double d2 = n2.DotProduct(f2.Origin);
            XYZ n3 = p3.Normal; double d3 = n3.DotProduct(p3.Origin);

            double det = n1.DotProduct(n2.CrossProduct(n3));
            if (Math.Abs(det) < 1e-6) return null;

            return (d1 * n2.CrossProduct(n3) + d2 * n3.CrossProduct(n1) + d3 * n1.CrossProduct(n2)) / det;
        }
    }

    public class LocalWarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor f in failuresAccessor.GetFailureMessages())
            {
                if (f.GetSeverity() == FailureSeverity.Warning) failuresAccessor.DeleteWarning(f);
            }
            return FailureProcessingResult.Continue;
        }
    }

    public class PartOnlyFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Part;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
