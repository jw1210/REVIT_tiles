using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace TilePlanner.Commands
{
    // ==========================================
    // [V4.1.3] 技術修訂版 - 零件延伸與平面分割技術
    // 邏輯：零件物理延伸 + 幾何平面交會切割
    // ==========================================
    public enum JoinType
    {
        OuterMiter,  // 陽角 - 45度斜接
        OuterCover,  // 陽角 - 正面蓋磚
        InnerButt,   // 陰角 - 密接/離縫
        InnerEmbed   // 陰角 - 結構嵌入
    }

    public class CornerSettingsDialog : System.Windows.Window
    {
        public double GapValue { get; private set; } = 2.0; 
        public JoinType SelectedJoinType { get; private set; } = JoinType.OuterMiter;

        private System.Windows.Controls.TextBox txtGap;
        private System.Windows.Controls.RadioButton rbOuterMiter, rbOuterCover, rbInnerButt, rbInnerEmbed;

        public CornerSettingsDialog()
        {
            this.Title = "TilePlanner - 轉角接合參數設定 (V4.1.3)";
            this.Width = 380; this.Height = 280;
            this.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            this.ResizeMode = System.Windows.ResizeMode.NoResize; this.Topmost = true;

            var grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(15) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var lblTitle = new System.Windows.Controls.TextBlock { Text = "請選擇轉角接合形式：", FontWeight = System.Windows.FontWeights.Bold, Margin = new System.Windows.Thickness(0, 0, 0, 10) };
            System.Windows.Controls.Grid.SetRow(lblTitle, 0); grid.Children.Add(lblTitle);

            var spRadios = new System.Windows.Controls.StackPanel();
            rbOuterMiter = new System.Windows.Controls.RadioButton { Content = "[陽角] 磨角斜接 (Mitered)", IsChecked = true, Margin = new System.Windows.Thickness(5) };
            rbOuterCover = new System.Windows.Controls.RadioButton { Content = "[陽角] 正面蓋磚 (Butt Join)", Margin = new System.Windows.Thickness(5) };
            rbInnerButt  = new System.Windows.Controls.RadioButton { Content = "[陰角] 密接 / 離縫", Margin = new System.Windows.Thickness(5) };
            rbInnerEmbed = new System.Windows.Controls.RadioButton { Content = "[陰角] 結構嵌入", Margin = new System.Windows.Thickness(5) };
            spRadios.Children.Add(rbOuterMiter); spRadios.Children.Add(rbOuterCover); spRadios.Children.Add(rbInnerButt); spRadios.Children.Add(rbInnerEmbed);
            System.Windows.Controls.Grid.SetRow(spRadios, 1); grid.Children.Add(spRadios);

            var spGap = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 10, 0, 10) };
            spGap.Children.Add(new System.Windows.Controls.TextBlock { Text = "預留灰縫間距 (mm): ", VerticalAlignment = System.Windows.VerticalAlignment.Center });
            txtGap = new System.Windows.Controls.TextBox { Text = "2", Width = 60, VerticalContentAlignment = System.Windows.VerticalAlignment.Center, Padding = new System.Windows.Thickness(2) };
            txtGap.Loaded += (s, e) => { txtGap.Focus(); txtGap.SelectAll(); };
            spGap.Children.Add(txtGap);
            System.Windows.Controls.Grid.SetRow(spGap, 2); grid.Children.Add(spGap);

            var spBtns = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var btnOk = new System.Windows.Controls.Button { Content = "確定", Width = 80, IsDefault = true };
            var btnCancel = new System.Windows.Controls.Button { Content = "取消", Width = 80, Margin = new System.Windows.Thickness(10, 0, 0, 0), IsCancel = true };
            
            void ConfirmAction()
            {
                if (double.TryParse(txtGap.Text, out double val) && val >= 2.0) {
                    GapValue = val;
                    if (rbOuterMiter.IsChecked == true) SelectedJoinType = JoinType.OuterMiter;
                    else if (rbOuterCover.IsChecked == true) SelectedJoinType = JoinType.OuterCover;
                    else if (rbInnerButt.IsChecked == true) SelectedJoinType = JoinType.InnerButt;
                    else SelectedJoinType = JoinType.InnerEmbed;
                    this.DialogResult = true; this.Close();
                } else {
                    System.Windows.MessageBox.Show("為確保幾何穩定性並避免產生無效網格，灰縫間距不得小於 2mm。", "設定無效", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    txtGap.Focus(); txtGap.SelectAll();
                }
            }

            btnOk.Click += (s, e) => ConfirmAction();
            this.PreviewKeyDown += (s, e) => {
                if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Return) { ConfirmAction(); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.Escape) { this.DialogResult = false; this.Close(); e.Handled = true; }
            };

            spBtns.Children.Add(btnOk); spBtns.Children.Add(btnCancel);
            System.Windows.Controls.Grid.SetRow(spBtns, 3); grid.Children.Add(spBtns);
            this.Content = grid;
        }
    }

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
                CornerSettingsDialog dialog = new CornerSettingsDialog();
                if (dialog.ShowDialog() != true) return Result.Cancelled;
                
                double gapFeet = dialog.GapValue / 304.8;
                JoinType joinType = dialog.SelectedJoinType;
                bool isMiter = joinType == JoinType.OuterMiter;
                
                string promptA = (isMiter || joinType == JoinType.InnerButt) 
                    ? "第 1 步：選取【第一側】磁磚零件 (完成後請按 Enter)" : "第 1 步：選取【覆蓋側】磁磚 (延伸目標)";
                string promptB = (isMiter || joinType == JoinType.InnerButt) 
                    ? "第 2 步：選取【第二側】磁磚零件 (完成後請按 Enter)" : "第 2 步：選取【退縮側】磁磚 (切割目標)";

                IList<Reference> refsA = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), promptA);
                if (refsA == null || refsA.Count == 0) return Result.Cancelled;

                IList<Reference> refsB = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), promptB);
                if (refsB == null || refsB.Count == 0) return Result.Cancelled;

                using (TransactionGroup tg = new TransactionGroup(doc, "手動轉角接合 V4.1.3"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "幾何延伸與切割執行"))
                    {
                        trans.Start();
                        var options = trans.GetFailureHandlingOptions();
                        options.SetFailuresPreprocessor(new LocalWarningSwallower());
                        trans.SetFailureHandlingOptions(options);

                        try
                        {
                            // 步驟 1：主體接合順序同步與零件預延伸
                            SynchronizeHostJoinOrder(doc, refsA[0].ElementId, refsB[0].ElementId, joinType);
                            
                            // 強置模型再生以更新零件幾何
                            doc.Regenerate(); 

                            Part partA = doc.GetElement(refsA[0].ElementId) as Part;
                            Part partB = doc.GetElement(refsB[0].ElementId) as Part;
                            Solid solidA = GetSolid(partA);
                            Solid solidB = GetSolid(partB);
                            
                            XYZ nA = GetLargestFaceNormal(solidA);
                            XYZ nB = GetLargestFaceNormal(solidB);
                            XYZ axis = (nA.CrossProduct(nB).GetLength() > 0.1) ? nA.CrossProduct(nB).Normalize() : nA.Normalize();

                            var facesA = GetSideFaces(solidA, axis);
                            var facesB = GetSideFaces(solidB, axis);

                            if (facesA.Count < 2 || facesB.Count < 2) throw new Exception("磁磚幾何分析失敗。");

                            XYZ origin = (solidA.ComputeCentroid() + solidB.ComputeCentroid()) / 2.0;
                            Plane interfacePlane = Plane.CreateByNormalAndOrigin(axis, origin);

                            List<XYZ> intersectPts = new List<XYZ>();
                            foreach (var fa in facesA) foreach (var fb in facesB) {
                                XYZ pt = IntersectThreePlanes(fa, fb, interfacePlane);
                                if (pt != null) intersectPts.Add(pt);
                            }

                            if (intersectPts.Count < 2) throw new Exception("交線點計算失敗。");

                            XYZ P1 = intersectPts[0], P2 = intersectPts[0];
                            double maxD = -1;
                            for (int i = 0; i < intersectPts.Count; i++) {
                                for (int j = i + 1; j < intersectPts.Count; j++) {
                                    double d = intersectPts[i].DistanceTo(intersectPts[j]);
                                    if (d > maxD) { maxD = d; P1 = intersectPts[i]; P2 = intersectPts[j]; }
                                }
                            }

                            XYZ outDir = (nA + nB).Normalize(); 
                            XYZ Vis, Bur;
                            if ((P1 - P2).DotProduct(outDir) > 0) { Vis = P1; Bur = P2; } else { Vis = P2; Bur = P1; }

                            XYZ dirA = axis.CrossProduct(nA).Normalize(); if (dirA.DotProduct(Vis - Bur) < 0) dirA = -dirA;
                            XYZ dirB = axis.CrossProduct(nB).Normalize(); if (dirB.DotProduct(Vis - Bur) < 0) dirB = -dirB;

                            double dot = dirA.DotProduct(dirB);
                            if (dot > 1.0) dot = 1.0; if (dot < -1.0) dot = -1.0;
                            double sinAngle = Math.Sin(Math.Acos(dot));
                            if (sinAngle < 0.01) sinAngle = 0.01;
                            double gapSlant = gapFeet / sinAngle;

                            // 步驟 2 & 3：向量化平面切割
                            if (joinType == JoinType.OuterMiter)
                            {
                                XYZ bisectorNormal = (nA + nB).Normalize();
                                double offset = (gapFeet / 2.0) / Math.Max(0.01, Math.Sin(Math.Acos(dot) / 2.0));
                                Plane planeA = Plane.CreateByNormalAndOrigin(bisectorNormal, Vis - dirA * offset);
                                Plane planeB = Plane.CreateByNormalAndOrigin(bisectorNormal, Vis - dirB * offset);
                                
                                foreach (var r in refsA) ExecutePlaneCut(doc, r.ElementId, planeA, Vis, axis);
                                foreach (var r in refsB) ExecutePlaneCut(doc, r.ElementId, planeB, Vis, axis);
                            }
                            else if (joinType == JoinType.OuterCover)
                            {
                                Plane planeB = Plane.CreateByNormalAndOrigin(nA, Vis - dirB * gapSlant);
                                foreach (var r in refsB) ExecutePlaneCut(doc, r.ElementId, planeB, Vis, axis);
                            }
                            else if (joinType == JoinType.InnerButt)
                            {
                                Plane planeA = Plane.CreateByNormalAndOrigin(nB, Vis);
                                Plane planeB = Plane.CreateByNormalAndOrigin(nA, Vis - dirB * gapSlant);
                                foreach (var r in refsA) ExecutePlaneCut(doc, r.ElementId, planeA, Bur, axis);
                                foreach (var r in refsB) ExecutePlaneCut(doc, r.ElementId, planeB, Bur, axis);
                            }
                            else if (joinType == JoinType.InnerEmbed)
                            {
                                Plane planeB = Plane.CreateByNormalAndOrigin(nA, Vis - dirB * gapSlant);
                                foreach (var r in refsB) ExecutePlaneCut(doc, r.ElementId, planeB, Bur, axis);
                            }

                            // 最終參數同步：開放造型控點
                            EnableShapeHandles(doc, refsA.Concat(refsB));

                            trans.Commit();
                        }
                        catch (Exception ex)
                        {
                            trans.RollBack(); TaskDialog.Show("執行錯誤", $"操作失敗：{ex.Message}"); tg.RollBack(); return Result.Failed;
                        }
                    }
                    tg.Assimilate();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("致命錯誤", ex.Message); return Result.Failed; }
        }

        private void SynchronizeHostJoinOrder(Document doc, ElementId partIdA, ElementId partIdB, JoinType joinType)
        {
            try
            {
                Wall wallA = GetHostWall(doc, partIdA);
                Wall wallB = GetHostWall(doc, partIdB);
                if (wallA == null || wallB == null) return;

                if (joinType == JoinType.OuterCover)
                {
                    if (JoinGeometryUtils.AreElementsJoined(doc, wallA, wallB))
                    {
                        if (JoinGeometryUtils.IsCuttingElementInJoin(doc, wallB, wallA))
                        {
                            JoinGeometryUtils.SwitchJoinOrder(doc, wallA, wallB);
                        }
                    }
                }
                else if (joinType == JoinType.OuterMiter || joinType == JoinType.InnerButt)
                {
                    LocationCurve lcA = wallA.Location as LocationCurve;
                    LocationCurve lcB = wallB.Location as LocationCurve;
                    if (lcA != null && lcB != null)
                    {
                        int endA = GetClosestEnd(lcA.Curve, lcB.Curve);
                        int endB = GetClosestEnd(lcB.Curve, lcA.Curve);
                        lcA.set_JoinType(endA, Autodesk.Revit.DB.JoinType.Miter);
                        lcB.set_JoinType(endB, Autodesk.Revit.DB.JoinType.Miter);
                    }
                }
            }
            catch { }
        }

        private Wall GetHostWall(Document doc, ElementId partId)
        {
            Part part = doc.GetElement(partId) as Part;
            if (part == null) return null;
            var linkIds = part.GetSourceElementIds();
            if (linkIds == null || linkIds.Count == 0) return null;
            return doc.GetElement(linkIds.First().HostElementId) as Wall;
        }

        private int GetClosestEnd(Curve c1, Curve c2)
        {
            double d00 = c1.GetEndPoint(0).DistanceTo(c2.GetEndPoint(0));
            double d01 = c1.GetEndPoint(0).DistanceTo(c2.GetEndPoint(1));
            double d10 = c1.GetEndPoint(1).DistanceTo(c2.GetEndPoint(0));
            double d11 = c1.GetEndPoint(1).DistanceTo(c2.GetEndPoint(1));
            double min = new[] { d00, d01, d10, d11 }.Min();
            return (min == d00 || min == d01) ? 0 : 1;
        }

        private const double DivisionVectorLength = 500.0;
        private void ExecutePlaneCut(Document doc, ElementId partId, Plane plane, XYZ targetForDelete, XYZ axis)
        {
            try 
            {
                SketchPlane sp = SketchPlane.Create(doc, plane);
                XYZ v = plane.Normal.CrossProduct(axis).Normalize();
                Line line = Line.CreateBound(plane.Origin - v * 100, plane.Origin + v * 100);
                
                PartUtils.DivideParts(doc, new List<ElementId> { partId }, new List<ElementId>(), new List<Curve> { line }, sp.Id);
                doc.Regenerate(); 

                var subParts = PartUtils.GetAssociatedParts(doc, partId, false, true);
                if (subParts.Count > 1) 
                {
                    ElementId toDelete = subParts.OrderBy(s => GetCentroid(doc.GetElement(s)).DistanceTo(targetForDelete)).First();
                    doc.Delete(toDelete);
                }
            } 
            catch { }
        }

        private void EnableShapeHandles(Document doc, IEnumerable<Reference> refs)
        {
            foreach (var r in refs)
            {
                Element e = doc.GetElement(r);
                if (e is Part part)
                {
                    try 
                    {
                        BuiltInParameter bip;
                        if (Enum.TryParse("DPART_SHOW_SHAPE_HANDLES", out bip))
                        {
                            Parameter p = part.get_Parameter(bip);
                            if (p != null && !p.IsReadOnly) p.Set(1);
                        }
                    }
                    catch { }
                }
            }
        }

        private XYZ GetCentroid(Element e) { BoundingBoxXYZ b = e.get_BoundingBox(null); return (b.Min + b.Max) / 2.0; }
        private Solid GetSolid(Part p) { return p.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine }).OfType<Solid>().FirstOrDefault(s => s.Volume > 0); }
        private XYZ GetLargestFaceNormal(Solid s) => s.Faces.OfType<PlanarFace>().OrderByDescending(f => f.Area).FirstOrDefault()?.FaceNormal ?? XYZ.BasisZ;
        private List<PlanarFace> GetSideFaces(Solid s, XYZ axis) => s.Faces.OfType<PlanarFace>().Where(f => Math.Abs(f.FaceNormal.DotProduct(axis)) < 0.1).OrderByDescending(f => f.Area).Take(2).ToList();
        private XYZ IntersectThreePlanes(PlanarFace f1, PlanarFace f2, Plane p3) {
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
            var failures = failuresAccessor.GetFailureMessages();
            if (failures.Count == 0) return FailureProcessingResult.Continue;
            bool resolvedError = false;
            foreach (var f in failures)
            {
                if (f.GetSeverity() == FailureSeverity.Warning) failuresAccessor.DeleteWarning(f);
                else if (f.GetSeverity() == FailureSeverity.Error)
                {
                    try 
                    {
                        var method = failuresAccessor.GetType().GetMethod("CanResolveErrors");
                        bool canResolve = (method != null) ? (bool)method.Invoke(failuresAccessor, null) : false;
                        if (canResolve) { failuresAccessor.ResolveFailure(f); resolvedError = true; }
                    }
                    catch { }
                }
            }
            return resolvedError ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }
    }

    public class PartOnlyFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Part;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
