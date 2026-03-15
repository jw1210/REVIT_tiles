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
                TaskDialog td = new TaskDialog("選擇收邊形式");
                td.MainInstruction = "請選擇您想要的轉角收邊形式：";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "45度切角 (Miter)", "選擇相鄰的兩塊磁磚，自動切分為45度角");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "蓋磚 / 平接 (Cover)", "先選擇「要蓋人(保持完整)」的磚，再選擇「被蓋(被切斷)」的磚");
                
                TaskDialogResult result = td.Show();
                if (result != TaskDialogResult.CommandLink1 && result != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;

                bool isMiter = (result == TaskDialogResult.CommandLink1);
                Part partA = null, partB = null;

                if (isMiter)
                {
                    IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), "請選擇 2 塊相鄰的磁磚零件進行 45 度切角");
                    if (refs.Count != 2) return Result.Cancelled;
                    partA = doc.GetElement(refs[0]) as Part;
                    partB = doc.GetElement(refs[1]) as Part;
                }
                else
                {
                    Reference refA = uidoc.Selection.PickObject(ObjectType.Element, new PartOnlyFilter(), "第 1 步：請選擇【要蓋人 (保持完整)】的磁磚");
                    Reference refB = uidoc.Selection.PickObject(ObjectType.Element, new PartOnlyFilter(), "第 2 步：請選擇【被蓋 (要被切斷)】的磁磚");
                    partA = doc.GetElement(refA) as Part;
                    partB = doc.GetElement(refB) as Part;
                }

                if (partA == null || partB == null || partA.Id == partB.Id) return Result.Cancelled;

                using (Transaction trans = new Transaction(doc, "局部轉角接合 (全向度)"))
                {
                    FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(new LocalWarningSwallower());
                    trans.SetFailureHandlingOptions(options);

                    trans.Start();

                    try
                    {
                        // 1. 取得三維幾何與最大表面
                        Solid solidA = GetSolid(partA);
                        Solid solidB = GetSolid(partB);
                        var facesA = GetTwoLargestFaces(solidA);
                        var facesB = GetTwoLargestFaces(solidB);

                        if (facesA.Count < 2 || facesB.Count < 2) throw new Exception("無法解析磁磚的表面幾何。");

                        XYZ nA = facesA[0].FaceNormal;
                        XYZ nB = facesB[0].FaceNormal;

                        // 2. 計算轉角切割基準軸 (支援牆壁與樓板)
                        XYZ axis = nA.CrossProduct(nB);
                        if (axis.GetLength() < 0.01) throw new Exception("選取的兩塊磁磚幾乎平行，無法形成轉角。");
                        axis = axis.Normalize();

                        XYZ origin = (solidA.ComputeCentroid() + solidB.ComputeCentroid()) / 2.0;
                        Plane sketchPlane = Plane.CreateByNormalAndOrigin(axis, origin);

                        // 3. 找出交接點並投影到 2D 草圖平面
                        List<XYZ> corners2D = new List<XYZ>();
                        foreach (var fa in facesA)
                        {
                            foreach (var fb in facesB)
                            {
                                XYZ pt = IntersectThreePlanes(fa, fb, sketchPlane);
                                if (pt != null) corners2D.Add(pt);
                            }
                        }

                        if (corners2D.Count < 4) throw new Exception("幾何運算失敗，未能形成完美的轉角。");

                        XYZ cA = ProjectToPlane(solidA.ComputeCentroid(), sketchPlane);
                        XYZ cB = ProjectToPlane(solidB.ComputeCentroid(), sketchPlane);
                        XYZ M = (cA + cB) / 2.0;

                        // 依據距離中心點的遠近判斷內角點(I)與外角點(O)
                        corners2D = corners2D.OrderBy(p => p.DistanceTo(M)).ToList();
                        XYZ I = corners2D.First(); 
                        XYZ O = corners2D.Last();  

                        // 4. 計算幾何切割線 (Cut Curve)
                        List<Curve> cutCurves = new List<Curve>();
                        if (isMiter)
                        {
                            XYZ dir = (O - I).Normalize();
                            cutCurves.Add(Line.CreateBound(I - dir * 10, O + dir * 10)); // 45度對角切
                        }
                        else
                        {
                            XYZ cutDir = nA.CrossProduct(axis).Normalize(); // 沿著 A 磚的內側邊緣平切
                            cutCurves.Add(Line.CreateBound(I - cutDir * 10, I + cutDir * 10));
                        }

                        // 5. 鎔鑄後重新切割
                        PartMaker pmMerge = PartUtils.CreateMergedPart(doc, new List<ElementId> { partA.Id, partB.Id });
                        doc.Regenerate(); 

                        ElementId newPartId = ElementId.InvalidElementId;
                        var allParts = new FilteredElementCollector(doc, doc.ActiveView.Id).OfClass(typeof(Part)).ToElements();
                        foreach (Part p in allParts)
                        {
                            var maker = PartUtils.GetAssociatedPartMaker(doc, p.Id);
                            if (maker != null && maker.Id == pmMerge.Id) { newPartId = p.Id; break; }
                        }

                        if (newPartId != ElementId.InvalidElementId)
                        {
                            SketchPlane sp = SketchPlane.Create(doc, sketchPlane);
                            PartUtils.DivideParts(doc, new List<ElementId> { newPartId }, new List<ElementId>(), cutCurves, sp.Id);
                        }
                        
                        trans.Commit();
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        message = $"處理失敗：{ex.Message}";
                        return Result.Failed;
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

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
