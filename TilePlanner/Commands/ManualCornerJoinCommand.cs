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
    // [V4.1 新增] 動態生成的 WPF 灰縫輸入視窗
    // 完全使用 C# 刻出介面，免除 XAML 檔案的依賴
    // ==========================================
    public class CornerGapDialog : System.Windows.Window
    {
        private System.Windows.Controls.TextBox txtGap;
        public double GapValue { get; private set; } = 1.0; // 預設 1mm

        public CornerGapDialog()
        {
            this.Title = "設定轉角灰縫";
            this.Width = 320;
            this.Height = 160;
            this.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            this.ResizeMode = System.Windows.ResizeMode.NoResize;
            this.Topmost = true;

            var grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(15) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var lbl = new System.Windows.Controls.TextBlock 
            { 
                Text = "請輸入轉角切角的灰縫間距 (mm):", 
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                FontSize = 14
            };
            System.Windows.Controls.Grid.SetRow(lbl, 0);
            grid.Children.Add(lbl);

            txtGap = new System.Windows.Controls.TextBox 
            { 
                Text = "1", // 預設值為 1mm
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = 14,
                Padding = new System.Windows.Thickness(2)
            };
            // 讓輸入框一開啟就全選數字，方便直接打字覆蓋
            txtGap.Loaded += (s, e) => { txtGap.Focus(); txtGap.SelectAll(); };
            System.Windows.Controls.Grid.SetRow(txtGap, 1);
            grid.Children.Add(txtGap);

            var sp = new System.Windows.Controls.StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal, 
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right, 
                Margin = new System.Windows.Thickness(0, 10, 0, 0) 
            };
            System.Windows.Controls.Grid.SetRow(sp, 2);
            
            var btnOk = new System.Windows.Controls.Button { Content = "確定", Width = 80, IsDefault = true };
            btnOk.Click += (s, e) => 
            {
                if (double.TryParse(txtGap.Text, out double val) && val >= 0) 
                {
                    GapValue = val;
                    this.DialogResult = true;
                    this.Close();
                } 
                else 
                {
                    System.Windows.MessageBox.Show("請輸入大於或等於 0 的有效數字！", "格式錯誤", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    txtGap.Focus();
                    txtGap.SelectAll();
                }
            };
            
            var btnCancel = new System.Windows.Controls.Button { Content = "取消", Width = 80, Margin = new System.Windows.Thickness(10, 0, 0, 0), IsCancel = true };
            
            sp.Children.Add(btnOk);
            sp.Children.Add(btnCancel);
            grid.Children.Add(sp);

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
                // 1. 選擇收邊形式
                TaskDialog td = new TaskDialog("局部轉角接合 (V3.9)");
                td.MainInstruction = "請選擇您想要的轉角收邊形式：";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "45度切角 (Miter)");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "蓋磚 / 平接 (Cover)");
                
                TaskDialogResult result = td.Show();
                if (result != TaskDialogResult.CommandLink1 && result != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;

                bool isMiter = (result == TaskDialogResult.CommandLink1);

                // ==========================================
                // 2. [V3.9 新增] 彈出灰縫間距輸入視窗
                // ==========================================
                CornerGapDialog gapDialog = new CornerGapDialog();
                if (gapDialog.ShowDialog() != true)
                {
                    return Result.Cancelled; // 使用者按了取消
                }
                
                // 將使用者輸入的 mm 轉換為 Revit 底層單位的英呎 (Feet)
                double gapFeet = gapDialog.GapValue / 304.8;
                
                // 3. 設定多重選取的提示語
                string promptA = isMiter ? 
                    "第 1 步：請拉框選取【第一側】的磁磚 (選完請按 Enter 鍵確認)" : 
                    "第 1 步：請拉框選取【要蓋人 (保持完整側)】的磁磚 (選完請按 Enter 鍵確認)";
                    
                string promptB = isMiter ? 
                    "第 2 步：請拉框選取【第二側】的磁磚 (選完請按 Enter 鍵確認)" : 
                    "第 2 步：請拉框選取【被切斷 (被覆蓋側)】的磁磚 (選完請按 Enter 鍵確認)";

                // 4. 執行分邊多選
                IList<Reference> refsA = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), promptA);
                if (refsA == null || refsA.Count == 0) return Result.Cancelled;

                IList<Reference> refsB = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), promptB);
                if (refsB == null || refsB.Count == 0) return Result.Cancelled;

                Part partA = doc.GetElement(refsA[0]) as Part;
                Part partB = doc.GetElement(refsB[0]) as Part;
                if (partA == null || partB == null) return Result.Cancelled;

                using (TransactionGroup tg = new TransactionGroup(doc, "局部轉角精準切割"))
                {
                    tg.Start();

                    try
                    {
                        // 5. 幾何運算：獲取最大表面並計算全向度交接軸
                        Solid solidA = GetSolid(partA);
                        Solid solidB = GetSolid(partB);
                        var facesA = GetTwoLargestFaces(solidA);
                        var facesB = GetTwoLargestFaces(solidB);

                        XYZ axis = null; PlanarFace bestFa = null, bestFb = null;
                        double maxCross = 0;
                        foreach (var fa in facesA)
                        {
                            foreach (var fb in facesB)
                            {
                                XYZ cross = fa.FaceNormal.CrossProduct(fb.FaceNormal);
                                double len = cross.GetLength();
                                if (len > maxCross) { maxCross = len; axis = cross; bestFa = fa; bestFb = fb; }
                            }
                        }

                        if (maxCross < 0.1 || axis == null) 
                            throw new Exception("選取的兩側磁磚幾近平行，無法形成轉角。");

                        axis = axis.Normalize();
                        XYZ origin = (solidA.ComputeCentroid() + solidB.ComputeCentroid()) / 2.0;
                        Plane sketchPlane = Plane.CreateByNormalAndOrigin(axis, origin);

                        // 6. 找出 2D 投影交接點 (I 內角, O 外角)
                        List<XYZ> corners2D = new List<XYZ>();
                        foreach (var fa in facesA)
                            foreach (var fb in facesB)
                            {
                                XYZ pt = IntersectThreePlanes(fa, fb, sketchPlane);
                                if (pt != null) corners2D.Add(pt);
                            }

                        XYZ cA = ProjectToPlane(solidA.ComputeCentroid(), sketchPlane);
                        XYZ cB = ProjectToPlane(solidB.ComputeCentroid(), sketchPlane);
                        XYZ M = (cA + cB) / 2.0;

                        corners2D = corners2D.OrderBy(p => p.DistanceTo(M)).ToList();
                        XYZ I = corners2D.First(); 
                        XYZ O = corners2D.Last();  

                        // 7. 計算精準切割刀 (納入使用者自訂的 gapFeet)
                        List<Curve> cutCurveA = new List<Curve>();
                        List<Curve> cutCurveB = new List<Curve>();

                        XYZ DirA = axis.CrossProduct(bestFa.FaceNormal).Normalize();
                        if (DirA.DotProduct(O - cA) < 0) DirA = -DirA;
                        XYZ DirB = axis.CrossProduct(bestFb.FaceNormal).Normalize();
                        if (DirB.DotProduct(O - cB) < 0) DirB = -DirB;

                        if (isMiter)
                        {
                            // 45度角：兩側皆向內退縮自訂間距的一半 (gapFeet / 2)
                            XYZ DirM = (O - I).Normalize();
                            XYZ N_cut_A = axis.CrossProduct(DirM).Normalize();
                            if (N_cut_A.DotProduct(cA - O) < 0) N_cut_A = -N_cut_A;
                            XYZ N_cut_B = axis.CrossProduct(DirM).Normalize();
                            if (N_cut_B.DotProduct(cB - O) < 0) N_cut_B = -N_cut_B;

                            XYZ ptA = O + N_cut_A * (gapFeet / 2.0);
                            XYZ ptB = O + N_cut_B * (gapFeet / 2.0);

                            cutCurveA.Add(Line.CreateBound(ptA - DirM * 50, ptA + DirM * 50));
                            cutCurveB.Add(Line.CreateBound(ptB - DirM * 50, ptB + DirM * 50));
                        }
                        else
                        {
                            // 蓋磚：A 保留完整，B 退縮至 A 內緣再扣掉自訂間距 (gapFeet)
                            XYZ ptA = O - DirA * 0.002; 
                            cutCurveA.Add(Line.CreateBound(ptA - bestFa.FaceNormal * 50, ptA + bestFa.FaceNormal * 50));

                            XYZ ptB = I - DirB * gapFeet;
                            cutCurveB.Add(Line.CreateBound(ptB - bestFb.FaceNormal * 50, ptB + bestFb.FaceNormal * 50));
                        }

                        // 8. 執行實體切割與拋棄餘料
                        using (Transaction tCut = new Transaction(doc, "切除轉角餘料"))
                        {
                            FailureHandlingOptions options = tCut.GetFailureHandlingOptions();
                            options.SetFailuresPreprocessor(new LocalWarningSwallower());
                            tCut.SetFailureHandlingOptions(options);

                            tCut.Start();
                            SketchPlane sp = SketchPlane.Create(doc, sketchPlane);
                            doc.Regenerate();

                            foreach (var refA in refsA)
                                try { PartUtils.DivideParts(doc, new List<ElementId> { refA.ElementId }, new List<ElementId>(), cutCurveA, sp.Id); } catch { }
                            
                            foreach (var refB in refsB)
                                try { PartUtils.DivideParts(doc, new List<ElementId> { refB.ElementId }, new List<ElementId>(), cutCurveB, sp.Id); } catch { }

                            doc.Regenerate(); 

                            // 刪除切除的三角形餘料
                            foreach (var refA in refsA)
                            {
                                var subParts = PartUtils.GetAssociatedParts(doc, refA.ElementId, false, true);
                                if (subParts.Count > 1)
                                {
                                    ElementId tipId = subParts.OrderBy(id => DistanceToO(doc, id, O)).First();
                                    try { doc.Delete(tipId); } catch { }
                                }
                            }
                            foreach (var refB in refsB)
                            {
                                var subParts = PartUtils.GetAssociatedParts(doc, refB.ElementId, false, true);
                                if (subParts.Count > 1)
                                {
                                    ElementId tipId = subParts.OrderBy(id => DistanceToO(doc, id, O)).First();
                                    try { doc.Delete(tipId); } catch { }
                                }
                            }
                            tCut.Commit();
                        }

                        tg.Assimilate(); 
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        tg.RollBack();
                        TaskDialog.Show("局部轉角 - 失敗", $"無法完成轉角接合。\n原因：{ex.Message}");
                        return Result.Failed;
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("錯誤", ex.Message); return Result.Failed; }
        }


        // --- 幾何輔助工具 ---
        private Solid GetSolid(Part part)
        {
            Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            foreach (GeometryObject geomObj in part.get_Geometry(opt))
                if (geomObj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 0) return solid;
            return null;
        }

        private List<PlanarFace> GetTwoLargestFaces(Solid solid)
        {
            var faces = new List<PlanarFace>();
            foreach (Face f in solid.Faces) if (f is PlanarFace pf) faces.Add(pf);
            return faces.OrderByDescending(f => f.Area).Take(2).ToList();
        }

        private XYZ ProjectToPlane(XYZ point, Plane plane)
        {
            return point - plane.Normal.DotProduct(point - plane.Origin) * plane.Normal;
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

        private double DistanceToO(Document d, ElementId id, XYZ O)
        {
            var p = d.GetElement(id) as Part;
            if (p == null) return double.MaxValue;
            XYZ c = (p.get_BoundingBox(null).Min + p.get_BoundingBox(null).Max) / 2.0;
            return c.DistanceTo(O);
        }
    }

    public class LocalWarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor f in failuresAccessor.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning) failuresAccessor.DeleteWarning(f);
            return FailureProcessingResult.Continue;
        }
    }

    public class PartOnlyFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Part;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
