using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace TilePlanner.Commands
{
    // --- 1. 靜默錯誤處理器 (僅攔截 1.6mm 微小廢料刪除警告) ---
    public class TinyPartFailureHandler : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            if (failures.Count == 0) return FailureProcessingResult.Continue;

            bool isResolved = false;
            foreach (FailureMessageAccessor failure in failures)
            {
                FailureSeverity severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                    isResolved = true;
                }
                else if (severity == FailureSeverity.Error)
                {
                    if (failure.HasResolutions())
                    {
                        failuresAccessor.ResolveFailure(failure);
                        isResolved = true;
                    }
                    else
                    {
                        return FailureProcessingResult.ProceedWithRollBack;
                    }
                }
            }
            return isResolved ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }
    }

    // --- 2. 主命令介面 ---
    [Transaction(TransactionMode.Manual)]
    public class ManualCornerJoinCommand : IExternalCommand
    {
        private enum JoinMode { Miter, Butt, Cancel }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // [V4.1.5 Developer Bypass] 
            if (!TilePlanner.Security.LicenseManager.Validate()) return Result.Failed;

            try
            {
                // 1. 選擇接合模式
                JoinMode mode = GetJoinModeFromUser();
                if (mode == JoinMode.Cancel) return Result.Cancelled;

                double gapFeet = 2.0 / 304.8; // 2mm 灰縫

                // 2. 執行選取流程
                string promptA = mode == JoinMode.Butt ? "選取 A 側零件 (蓋磚側/不退縮側) - 選取完畢請按 Enter" : "選取 A 側零件 - 選取完畢請按 Enter";
                string promptB = mode == JoinMode.Butt ? "選取 B 側零件 (被蓋側/退縮側) - 選取完畢請按 Enter" : "選取 B 側零件 - 選取完畢請按 Enter";

                IList<Reference> refsA = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), promptA);
                if (refsA.Count == 0) return Result.Cancelled;

                IList<Reference> refsB = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), promptB);
                if (refsB.Count == 0) return Result.Cancelled;

                // 3. 執行切割 Transaction
                using (Transaction trans = new Transaction(doc, $"磁磚計畫 V4.1.11 - {(mode == JoinMode.Miter ? "雙側切角" : "蓋磚")}"))
                {
                    trans.Start();

                    // 綁定微小廢料處理器
                    FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(new TinyPartFailureHandler());
                    options.SetClearAfterRollback(true);
                    trans.SetFailureHandlingOptions(options);

                    Part basePartA = doc.GetElement(refsA.First()) as Part;
                    Part basePartB = doc.GetElement(refsB.First()) as Part;
                    Solid solidA = GetSolid(basePartA);
                    Solid solidB = GetSolid(basePartB);

                    // 取得法向量與幾何交點
                    XYZ nA = GetOuterVerticalFaceNormal(solidA);
                    XYZ nB = GetOuterVerticalFaceNormal(solidB);
                    XYZ cornerOrigin = GetTrueCornerOrigin(basePartA, basePartB, nA, nB);

                    // 4. 模組分流
                    if (mode == JoinMode.Miter)
                    {
                        ExecuteMiterJoin(doc, refsA, refsB, nA, nB, cornerOrigin, gapFeet, basePartA, basePartB);
                    }
                    else if (mode == JoinMode.Butt)
                    {
                        ExecuteButtJoin(doc, refsA, refsB, nA, nB, cornerOrigin, gapFeet, basePartA);
                    }

                    trans.Commit();
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", ex.Message);
                return Result.Failed;
            }
        }

        // --- 模組 A：雙側切角 (Miter Join) ---
        private void ExecuteMiterJoin(Document doc, IList<Reference> refsA, IList<Reference> refsB, XYZ nA, XYZ nB, XYZ origin, double gapFeet, Part baseA, Part baseB)
        {
            // 計算角平分線方向與垂直偏移方向
            XYZ cutDir = (nA + nB).Normalize();
            XYZ shiftDir = XYZ.BasisZ.CrossProduct(cutDir).Normalize();

            // 雙側各退縮 1mm (gapFeet/2)
            XYZ p1 = origin + shiftDir * (gapFeet / 2.0);
            XYZ p2 = origin - shiftDir * (gapFeet / 2.0);

            XYZ centA = GetCentroid(baseA);
            XYZ centB = GetCentroid(baseB);
            
            // 距離判定：將正確的切割基準點指派給對應的零件
            XYZ originA = (p1.DistanceTo(centA) < p2.DistanceTo(centA)) ? p1 : p2;
            XYZ originB = (p1.DistanceTo(centB) < p2.DistanceTo(centB)) ? p1 : p2;

            foreach (Reference r in refsA) ExecutePlanViewCut(doc, r.ElementId, originA, cutDir, origin);
            foreach (Reference r in refsB) ExecutePlanViewCut(doc, r.ElementId, originB, cutDir, origin);
        }

        // --- 模組 B：蓋磚 (Butt Join) ---
        private void ExecuteButtJoin(Document doc, IList<Reference> refsA, IList<Reference> refsB, XYZ nA, XYZ nB, XYZ origin, double gapFeet, Part baseA)
        {
            // A 側 (蓋磚側)：切割平面平行於 B 表面，原點位於轉角最外緣。
            XYZ cutDirA = XYZ.BasisZ.CrossProduct(nB).Normalize();
            XYZ originA = origin;

            // B 側 (退縮側)：切割平面平行於 A 表面，原點向 A 的反向法向量退縮 (A磚厚度 + 灰縫)。
            XYZ cutDirB = XYZ.BasisZ.CrossProduct(nA).Normalize();
            double thicknessA = GetPartThickness(baseA, nA);
            
            XYZ originB = origin - nA * (thicknessA + gapFeet);

            foreach (Reference r in refsA) ExecutePlanViewCut(doc, r.ElementId, originA, cutDirA, origin);
            foreach (Reference r in refsB) ExecutePlanViewCut(doc, r.ElementId, originB, cutDirB, origin);
        }

        // --- UI 選項 ---
        private JoinMode GetJoinModeFromUser()
        {
            TaskDialog td = new TaskDialog("局部轉角接合")
            {
                MainInstruction = "請選擇轉角接合模式",
                MainContent = "灰縫間距已統一預設為 2mm。\n請依照提示選取零件並按 Enter 確認。",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "雙側切角 (Miter Join)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "蓋磚 (Butt Join) - 先選的蓋人");

            TaskDialogResult result = td.Show();
            if (result == TaskDialogResult.CommandLink1) return JoinMode.Miter;
            if (result == TaskDialogResult.CommandLink2) return JoinMode.Butt;
            return JoinMode.Cancel;
        }

        // --- 核心切割引擎 ---
        private void ExecutePlanViewCut(Document doc, ElementId partId, XYZ cutOrigin, XYZ cutDir, XYZ cornerOrigin)
        {
            try
            {
                Plane horizontalPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, cutOrigin);
                SketchPlane sp = SketchPlane.Create(doc, horizontalPlane);
                Line cutLine = Line.CreateBound(cutOrigin - cutDir * 50.0, cutOrigin + cutDir * 50.0);
                
                PartUtils.DivideParts(doc, new List<ElementId> { partId }, new List<ElementId>(), new List<Curve> { cutLine }, sp.Id);
                doc.Regenerate();

                // 廢料清理
                ICollection<ElementId> subParts = PartUtils.GetAssociatedParts(doc, partId, false, true);
                if (subParts.Count > 1)
                {
                    ElementId wasteId = subParts.OrderBy(id => GetCentroid(doc.GetElement(id)).DistanceTo(cornerOrigin)).First();
                    if(doc.GetElement(wasteId) != null) doc.Delete(wasteId);
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex) 
            { 
                // 修正：解除靜默攔截。若 Revit 幾何引擎拒絕切割，將顯示明確原因以供診斷。
                TaskDialog.Show("切割失敗診斷", $"零件 ID {partId} 無法分割。\n原因: {ex.Message}\n\n說明: 切割線未貫穿實體幾何。請確認欲切割的磁磚是否具備足夠的延伸長度或交疊面積。");
            }
        }

        // --- 幾何解析方法 ---
        private XYZ GetOuterVerticalFaceNormal(Solid s)
        {
            return s.Faces.OfType<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 0.01)
                .OrderByDescending(f => f.Area)
                .First().FaceNormal.Normalize();
        }

        private double GetPartThickness(Part part, XYZ outerNormal)
        {
            Solid s = GetSolid(part);
            
            // 修正：放寬法向量匹配容差至 0.98，以確保能正確辨識平行之內外表面。
            var backFace = s.Faces.OfType<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 0.01 && f.FaceNormal.DotProduct(outerNormal) < -0.98)
                .OrderByDescending(f => f.Area)
                .FirstOrDefault();

            var frontFace = s.Faces.OfType<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 0.01 && f.FaceNormal.DotProduct(outerNormal) > 0.98)
                .OrderByDescending(f => f.Area)
                .FirstOrDefault();

            if (frontFace != null && backFace != null)
            {
                return Math.Abs((frontFace.Origin - backFace.Origin).DotProduct(outerNormal));
            }
            return 0.05; // 計算失敗時之備用容錯厚度
        }

        private XYZ GetTrueCornerOrigin(Part pA, Part pB, XYZ nA, XYZ nB)
        {
            Solid sA = GetSolid(pA); Solid sB = GetSolid(pB);
            XYZ originA = sA.Faces.OfType<PlanarFace>().OrderByDescending(f => f.Area).First().Origin;
            XYZ originB = sB.Faces.OfType<PlanarFace>().OrderByDescending(f => f.Area).First().Origin;

            // 設定交點高程為兩零件重心 Z 軸平均值
            double z = (GetCentroid(pA).Z + GetCentroid(pB).Z) / 2.0;

            // 修正：將 3D 坐標降維至 XY 平面，透過克拉瑪法則計算 2D 交點，消除高程干擾。
            double d1 = nA.X * originA.X + nA.Y * originA.Y;
            double d2 = nB.X * originB.X + nB.Y * originB.Y;
            double det = nA.X * nB.Y - nA.Y * nB.X;

            if (Math.Abs(det) > 1e-6)
            {
                double x = (d1 * nB.Y - d2 * nA.Y) / det;
                double y = (nA.X * d2 - nB.X * d1) / det;
                return new XYZ(x, y, z);
            }
            return (GetCentroid(pA) + GetCentroid(pB)) / 2.0; 
        }

        private Solid GetSolid(Part p) => p.get_Geometry(new Options() { ComputeReferences = true }).OfType<Solid>().FirstOrDefault(s => s.Volume > 0);
        private XYZ GetCentroid(Element e) { BoundingBoxXYZ b = e.get_BoundingBox(null); return b != null ? (b.Min + b.Max) / 2.0 : XYZ.Zero; }
    }

    public class PartOnlyFilter : ISelectionFilter {
        public bool AllowElement(Element e) => e is Part;
        public bool AllowReference(Reference r, XYZ p) => true;
    }
}
