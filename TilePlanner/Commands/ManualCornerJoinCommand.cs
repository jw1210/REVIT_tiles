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
    // [V4.1.4] 物理自適應版 - 真實幾何切割科技
    // 不需要零件預先延伸，只要兩塊磚在轉角有實體接觸，即可精準收頭。
    // 1. 真實垂直法向分析 (GetOuterVerticalFaceNormal)
    // 2. 克拉瑪法則精準座標交點 (GetTrueCornerOrigin)
    // 3. 垂直雷射鎖定切割 (ExecuteVerticalCut / BasisZ)
    // ==========================================
    public enum JoinType
    {
        OuterMiter,  // 陽角 - 磨角斜接
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
            this.Title = "TilePlanner - 轉角接合設定 (V4.1.4 物理自適應)";
            this.Width = 380; this.Height = 280;
            this.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            this.ResizeMode = System.Windows.ResizeMode.NoResize; this.Topmost = true;

            var grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(15) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var lblTitle = new System.Windows.Controls.TextBlock { Text = "請選擇接頭形式與參數：", FontWeight = System.Windows.FontWeights.Bold, Margin = new System.Windows.Thickness(0, 0, 0, 10) };
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
            txtGap = new System.Windows.Controls.TextBox { Text = "2.0", Width = 60, VerticalContentAlignment = System.Windows.VerticalAlignment.Center, Padding = new System.Windows.Thickness(2) };
            txtGap.Loaded += (s, e) => { txtGap.Focus(); txtGap.SelectAll(); };
            spGap.Children.Add(txtGap);
            System.Windows.Controls.Grid.SetRow(spGap, 2); grid.Children.Add(spGap);

            var spBtns = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var btnOk = new System.Windows.Controls.Button { Content = "確定執行", Width = 90, Height = 25, IsDefault = true };
            var btnCancel = new System.Windows.Controls.Button { Content = "取消", Width = 80, Height = 25, Margin = new System.Windows.Thickness(10, 0, 0, 0), IsCancel = true };
            
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
                    System.Windows.MessageBox.Show("為確保幾何穩定性並避免產生無效切屑，灰縫不得小於 2.0 mm。", "設定無效", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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

                Reference refA = uidoc.Selection.PickObject(ObjectType.Element, new PartOnlyFilter(), "第 1 步：選取 A 側磁磚 (零件)");
                Reference refB = uidoc.Selection.PickObject(ObjectType.Element, new PartOnlyFilter(), "第 2 步：選取 B 側磁磚 (零件)");

                using (Transaction trans = new Transaction(doc, "磁磚計畫 V4.1.4 真實幾何切割"))
                {
                    trans.Start();
                    var options = trans.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(new LocalWarningSwallower());
                    trans.SetFailureHandlingOptions(options);

                    try
                    {
                        Part partA = doc.GetElement(refA) as Part;
                        Part partB = doc.GetElement(refB) as Part;
                        Solid solidA = GetSolid(partA);
                        Solid solidB = GetSolid(partB);

                        // 1. 取得真實垂直面的法向量 (Real Surface Normal)
                        XYZ nA = GetOuterVerticalFaceNormal(solidA);
                        XYZ nB = GetOuterVerticalFaceNormal(solidB);

                        // 2. 計算真實轉角交點 (Cramer's Rule Vertex)
                        XYZ origin = GetTrueCornerOrigin(partA, partB, nA, nB);

                        if (joinType == JoinType.OuterMiter)
                        {
                            // 3. 計算角平分線法向量 (Bisector Plane Normal)
                            XYZ planeNormal = (nA - nB).Normalize();

                            // 4. 計算偏移位置 (向內退縮 1.0mm)
                            XYZ p1 = origin + planeNormal * (gapFeet / 2.0);
                            XYZ p2 = origin - planeNormal * (gapFeet / 2.0);

                            // 5. 絕對防呆：誰離重心近，誰就是專屬切刀
                            XYZ centA = GetCentroid(partA);
                            XYZ centB = GetCentroid(partB);
                            
                            Plane planeA = (p1.DistanceTo(centA) < p2.DistanceTo(centA)) 
                                ? Plane.CreateByNormalAndOrigin(planeNormal, p1) 
                                : Plane.CreateByNormalAndOrigin(planeNormal, p2);

                            Plane planeB = (p1.DistanceTo(centB) < p2.DistanceTo(centB)) 
                                ? Plane.CreateByNormalAndOrigin(planeNormal, p1) 
                                : Plane.CreateByNormalAndOrigin(planeNormal, p2);

                            // 6. 執行絕對垂直切割
                            ExecuteVerticalCut(doc, partA.Id, planeA, origin);
                            ExecuteVerticalCut(doc, partB.Id, planeB, origin);
                        }
                        else if (joinType == JoinType.OuterCover)
                        {
                            // A 磚不切，B 磚退縮。切刀面 = A 磚外皮 (nA) 向 B 磚重心偏移 gapFeet
                            XYZ centB = GetCentroid(partB);
                            XYZ pB = origin - nA * gapFeet; 
                            Plane planeB = Plane.CreateByNormalAndOrigin(nA, pB);
                            ExecuteVerticalCut(doc, partB.Id, planeB, origin);
                        }
                        else if (joinType == JoinType.InnerButt || joinType == JoinType.InnerEmbed)
                        {
                            // 陰角邏輯：A 磚貼齊 B 磚外皮，B 磚退縮 gapFeet
                            Plane planeA = Plane.CreateByNormalAndOrigin(nB, origin);
                            Plane planeB = Plane.CreateByNormalAndOrigin(nA, origin - nA * gapFeet);
                            ExecuteVerticalCut(doc, partA.Id, planeA, origin);
                            ExecuteVerticalCut(doc, partB.Id, planeB, origin);
                        }

                        EnableShapeHandles(doc, new List<Reference>{refA, refB});
                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack(); TaskDialog.Show("幾何算圖錯誤", $"執行失敗：{ex.Message}"); return Result.Failed;
                    }
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("致命例外", ex.Message); return Result.Failed; }
        }

        private void ExecuteVerticalCut(Document doc, ElementId partId, Plane plane, XYZ cornerPoint)
        {
            try
            {
                // 強制鎖死垂直切線，避免切出楔形 (BasisZ Knife)
                XYZ vDir = XYZ.BasisZ;
                Line cutLine = Line.CreateBound(plane.Origin - vDir * 50, plane.Origin + vDir * 50);
                
                SketchPlane sp = SketchPlane.Create(doc, plane);
                PartUtils.DivideParts(doc, new List<ElementId> { partId }, new List<ElementId>(), new List<Curve> { cutLine }, sp.Id);
                doc.Regenerate();

                // 智慧清理：刪除靠近外角點的虛脫廢料
                var subParts = PartUtils.GetAssociatedParts(doc, partId, false, true);
                if (subParts.Count > 1)
                {
                    ElementId waste = subParts.OrderBy(id => GetCentroid(doc.GetElement(id)).DistanceTo(cornerPoint)).First();
                    doc.Delete(waste);
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        }

        private XYZ GetOuterVerticalFaceNormal(Solid s)
        {
            var verticalFaces = s.Faces.OfType<PlanarFace>()
                .Where(f => Math.Abs(f.FaceNormal.Z) < 0.01) // 過濾垂直面
                .OrderByDescending(f => f.Area)
                .ToList();
            if (verticalFaces.Count == 0) return XYZ.BasisX;
            return verticalFaces.First().FaceNormal.Normalize();
        }

        private XYZ GetTrueCornerOrigin(Part pA, Part pB, XYZ nA, XYZ nB)
        {
            Solid sA = GetSolid(pA); Solid sB = GetSolid(pB);
            XYZ originA = sA.Faces.OfType<PlanarFace>().OrderByDescending(f => f.Area).First().Origin;
            XYZ originB = sB.Faces.OfType<PlanarFace>().OrderByDescending(f => f.Area).First().Origin;

            double z = (GetCentroid(pA).Z + GetCentroid(pB).Z) / 2.0;

            // 克拉瑪法則 (Cramer's Rule) 求 X,Y 交點
            double d1 = nA.DotProduct(originA);
            double d2 = nB.DotProduct(originB);
            double det = nA.X * nB.Y - nA.Y * nB.X;

            if (Math.Abs(det) > 1e-6)
            {
                double x = (d1 * nB.Y - d2 * nA.Y) / det;
                double y = (nA.X * d2 - nB.X * d1) / det;
                return new XYZ(x, y, z);
            }
            return (GetCentroid(pA) + GetCentroid(pB)) / 2.0; 
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

        private Solid GetSolid(Part p) => p.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine }).OfType<Solid>().FirstOrDefault(s => s.Volume > 0);
        private XYZ GetCentroid(Element e) { BoundingBoxXYZ b = e.get_BoundingBox(null); return (b.Min + b.Max) / 2.0; }
    }

    public class LocalWarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            foreach (var f in failures)
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
