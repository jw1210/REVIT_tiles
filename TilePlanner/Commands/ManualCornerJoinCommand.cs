using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.Attributes;

namespace TilePlanner.Commands
{
    // 靜默處理器
    public class AutoDeleteFailureHandler : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            if (failures.Count == 0) return FailureProcessingResult.Continue;
            foreach (FailureMessageAccessor failure in failures)
            {
                if (failure.GetSeverity() == FailureSeverity.Warning) failuresAccessor.DeleteWarning(failure);
                else if (failure.GetSeverity() == FailureSeverity.Error && failure.HasResolutions()) failuresAccessor.ResolveFailure(failure);
            }
            return FailureProcessingResult.ProceedWithCommit;
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

            using (TransactionGroup tg = new TransactionGroup(doc, "雙側切角 V4.1.20"))
            {
                tg.Start();
                try
                {
                    double offsetFeet = 0.0;

                    // 1. 選取零件
                    IList<Reference> refsA = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), "選取 A 側磁磚 (按 Enter 結束)");
                    IList<Reference> refsB = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), "選取 B 側磁磚 (按 Enter 結束)");
                    if (refsA == null || refsB == null || refsA.Count == 0 || refsB.Count == 0) return Result.Cancelled;

                    // 2. 幾何收集
                    var partsA = refsA.Select(r => doc.GetElement(r) as Part).Where(p => p != null).OrderBy(p => GetCentroid(p).Z).ToList();
                    var partsB = refsB.Select(r => doc.GetElement(r) as Part).Where(p => p != null).OrderBy(p => GetCentroid(p).Z).ToList();

                    // 3. 獲取主體牆與厚度
                    Wall wallA = GetHostWall(partsA.First());
                    Wall wallB = GetHostWall(partsB.First());
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
                    // [V4.1.20] 準備階段：解開接合併延伸牆體 (Flush to Outer face)
                    // -------------------------------------------------------------------------
                    using (Transaction tPrep = new Transaction(doc, "延伸牆體"))
                    {
                        tPrep.Start();
                        ExtendWallToIncludeCorner(wallA, intersectXY, wallB.Width);
                        ExtendWallToIncludeCorner(wallB, intersectXY, wallA.Width);
                        doc.Regenerate();
                        tPrep.Commit();
                    }

                    // 4. 計算 Miter 基準
                    XYZ nA_ref = GetDominantFaceNormal(partsA.First());
                    XYZ nB_ref = GetDominantFaceNormal(partsB.First());
                    XYZ miterNormal = (nA_ref - nB_ref).Normalize();

                    // -------------------------------------------------------------------------
                    // [V4.1.20] 分側序列執行
                    // -------------------------------------------------------------------------
                    
                    // Phase A
                    int successA = 0;
                    using (Transaction tA = new Transaction(doc, "Phase A Split"))
                    {
                        tA.Start();
                        tA.SetFailureHandlingOptions(tA.GetFailureHandlingOptions().SetFailuresPreprocessor(new AutoDeleteFailureHandler()));
                        foreach (Part p in partsA)
                        {
                            XYZ origin = new XYZ(intersectXY.X, intersectXY.Y, GetCentroid(p).Z);
                            if (CutAndExcludeWasteZeroGap(doc, p.Id, origin, miterNormal, offsetFeet)) successA++;
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
                            XYZ origin = new XYZ(intersectXY.X, intersectXY.Y, GetCentroid(p).Z);
                            if (CutAndExcludeWasteZeroGap(doc, p.Id, origin, miterNormal, offsetFeet)) successB++;
                        }
                        doc.Regenerate();
                        tB.Commit();
                    }

                    tg.Assimilate();
                    TaskDialog.Show("TilePlanner V4.1.20", $"完成！\nA 成功：{successA}\nB 成功：{successB}\n(已套用對位延伸與斜切修正)");
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

        private void ExtendWallToIncludeCorner(Wall wall, XYZ corner, double extensionDist)
        {
            LocationCurve lc = wall.Location as LocationCurve;
            if (lc == null) return;
            Curve curve = lc.Curve;
            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);

            int endIdx = (p0.DistanceTo(corner) < p1.DistanceTo(corner)) ? 0 : 1;
            
            // 1. 解開接合
            WallUtils.DisallowWallJoinAtEnd(wall, endIdx);
            
            // 2. 物理延伸 LocationLine
            XYZ endPt = curve.GetEndPoint(endIdx);
            XYZ otherPt = curve.GetEndPoint(1 - endIdx);
            XYZ dir = (endPt - otherPt).Normalize();
            
            XYZ newEnd = endPt + dir * extensionDist;
            if (endIdx == 0) lc.Curve = Line.CreateBound(newEnd, p1);
            else lc.Curve = Line.CreateBound(p0, newEnd);
        }

        private bool CutAndExcludeWasteZeroGap(Document doc, ElementId partId, XYZ origin, XYZ miterNormal, double offset)
        {
            Part p = doc.GetElement(partId) as Part;
            if (p == null) return false;

            XYZ centroid = GetCentroid(p);
            XYZ vecToCentroid = (centroid - origin).Normalize();
            
            XYZ inwardDir = (miterNormal.DotProduct(vecToCentroid) > 0) ? miterNormal : -miterNormal;
            XYZ cutPos = origin + inwardDir * offset; 

            Plane plane = Plane.CreateByNormalAndOrigin(inwardDir, cutPos);
            SketchPlane sp = SketchPlane.Create(doc, plane);
            XYZ axis = XYZ.BasisZ;
            Line cutLine = Line.CreateBound(cutPos - axis * 2500.0, cutPos + axis * 2500.0);

            try
            {
                PartUtils.DivideParts(doc, new List<ElementId> { partId }, new List<ElementId>(), new List<Curve> { cutLine }, sp.Id);
                doc.Regenerate();

                ICollection<ElementId> results = PartUtils.GetAssociatedParts(doc, partId, false, true);
                foreach (ElementId rid in results)
                {
                    Part sub = doc.GetElement(rid) as Part;
                    if (sub == null) continue;
                    XYZ vecSub = (GetCentroid(sub) - cutPos);
                    if (vecSub.DotProduct(inwardDir) < -1e-6)
                    {
                        sub.get_Parameter(BuiltInParameter.DPART_EXCLUDED)?.Set(1);
                    }
                }
                return true;
            }
            catch { return false; }
        }

        private Wall GetHostWall(Part p)
        {
            Element current = p;
            while (current is Part part)
            {
                var ids = part.GetSourceElementIds();
                if (ids == null || ids.Count == 0) break;
                Element next = p.Document.GetElement(ids.First().HostElementId);
                if (next == null) break;
                if (next is Wall wall) return wall;
                current = next;
            }
            return null;
        }

        private XYZ GetDominantFaceNormal(Part p)
        {
            Solid s = GetSolid(p);
            if (s == null) return XYZ.BasisZ;
            return s.Faces.OfType<PlanarFace>().OrderByDescending(f => f.Area).First().FaceNormal;
        }

        private XYZ GetCentroid(Element e)
        {
            BoundingBoxXYZ b = e.get_BoundingBox(null);
            return b != null ? (b.Min + b.Max) / 2.0 : XYZ.Zero;
        }

        private Solid GetSolid(Part p) => p.get_Geometry(new Options()).OfType<Solid>().FirstOrDefault(s => s.Volume > 0);
    }

    public class PartOnlyFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Part;
        public bool AllowReference(Reference r, XYZ p) => true;
    }
}
