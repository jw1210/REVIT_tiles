using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core
{
    public class TilePartEngine
    {
        private readonly Document _doc;
        private readonly TileConfig _config;

        public TilePartEngine(Document doc, TileConfig config)
        {
            _doc = doc;
            _config = config;
        }

        public void ExecuteOnElement(Element hostElement)
        {
            // 1. Check if valid for parts
            if (!PartUtils.AreElementsValidForCreateParts(_doc, new List<ElementId> { hostElement.Id }))
            {
                throw new InvalidOperationException("所選取的物件不支援建立零件 (Parts)。請確認點選的是實體牆或實體樓板。");
            }

            // 2. Create Parts
            var elementList = new List<ElementId> { hostElement.Id };
            PartUtils.CreateParts(_doc, elementList);
            _doc.Regenerate();

            // 3. Find newly created Part associated with the original element
            ICollection<ElementId> partIds = PartUtils.GetAssociatedParts(_doc, hostElement.Id, true, true);
            if (partIds.Count == 0)
            {
                throw new InvalidOperationException("建立零件失敗。");
            }

            // Assuming a basic wall or floor, there might be multiple parts (one per layer). 
            // We want the exterior/finish face part. For simplicity, we'll try to divide the part that has the thickest dimension or the outermost one. 
            // For a single-layer finish wall, there's only 1 part.
            ElementId targetPartId = partIds.FirstOrDefault();
            Part targetPart = _doc.GetElement(targetPartId) as Part;

            // 4. Extract Face to draw division lines upon
            Options opt = new Options { ComputeReferences = true };
            GeometryElement geomElem = targetPart.get_Geometry(opt);
            
            Solid solid = null;
            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid s && s.Faces.Size > 0)
                {
                    solid = s;
                    break;
                }
            }

            if (solid == null) throw new InvalidOperationException("無法解析零件的幾何實體。");

            // Find the largest planar face (usually the front face of the finish wall/floor)
            PlanarFace targetFace = null;
            double maxArea = 0;
            
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    if (pf.Area > maxArea)
                    {
                        maxArea = pf.Area;
                        targetFace = pf;
                    }
                }
            }

            if (targetFace == null) throw new InvalidOperationException("找不到足夠面積的平整面來進行磁磚分割。");

            // 5. Generate intersecting curves based on TileConfig
            IList<Curve> intersectingCurves = GenerateCurves(targetFace, hostElement is Wall);

            if (intersectingCurves.Count == 0) return;

            // 6. Execute DivideParts
            // Setup SketchPlane for the intersecting curves (usually needs to intersect the part)
            SketchPlane sketchPlane = SketchPlane.Create(_doc, targetFace.GetSurface() as Plane);

            // Grout Gap
            double groutGap = _config.GroutWidthFeet;

            // Use the target part
            List<ElementId> partsToDivide = new List<ElementId> { targetPartId };

            // We must provide intersecting curve arrays. Since PartUtils takes an ICollection<Curve> we pass our generated curves
            try
            {
                PartUtils.DivideParts(
                    _doc, 
                    partsToDivide, 
                    intersectingCurves, 
                    sketchPlane.Id, 
                    ElementId.InvalidElementId, // No intersecting ElementIds
                    groutGap 
                );
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"切割零件時發生錯誤：{ex.Message}");
            }
        }

        private IList<Curve> GenerateCurves(PlanarFace face, bool isWall)
        {
            List<Curve> curves = new List<Curve>();
            BoundingBoxUV bbox = face.GetBoundingBox();

            double widthUV = bbox.Max.U - bbox.Min.U;
            double heightUV = bbox.Max.V - bbox.Min.V;

            double uDist = _config.CellWidthFeet;
            double vDist = _config.CellHeightFeet;

            // Create U grid lines (V=const) - Horizontal cuts
            int numHRows = (int)Math.Ceiling(heightUV / vDist) + 1;
            for (int i = 1; i <= numHRows; i++)
            {
                double vPos = bbox.Min.V + i * vDist;
                if (vPos >= bbox.Max.V - 0.001) break;

                XYZ p1 = face.Evaluate(new UV(bbox.Min.U - 1.0, vPos));
                XYZ p2 = face.Evaluate(new UV(bbox.Max.U + 1.0, vPos));
                curves.Add(Line.CreateBound(p1, p2));
            }

            // Create V grid lines (U=const) - Vertical cuts
            if (_config.PatternType == TilePatternType.Grid)
            {
                int numCols = (int)Math.Ceiling(widthUV / uDist) + 1;
                for (int i = 1; i <= numCols; i++)
                {
                    double uPos = bbox.Min.U + i * uDist;
                    if (uPos >= bbox.Max.U - 0.001) break;

                    XYZ p1 = face.Evaluate(new UV(uPos, bbox.Min.V - 1.0));
                    XYZ p2 = face.Evaluate(new UV(uPos, bbox.Max.V + 1.0));
                    curves.Add(Line.CreateBound(p1, p2));
                }
            }
            else // Running Bond
            {
                double offsetAmount = uDist * _config.RunningBondOffset;

                for (int r = 0; r <= numHRows; r++)
                {
                    double currentVLine = bbox.Min.V + r * vDist;
                    double nextVLine = bbox.Min.V + (r + 1) * vDist;
                    
                    // Allow cut lines to cleanly traverse the band height.
                    bool isEvenRow = (r % 2 == 0);
                    double shift = isEvenRow ? 0 : offsetAmount;

                    int numCols = (int)Math.Ceiling(widthUV / uDist) + 2;

                    for (int c = -1; c <= numCols; c++)
                    {
                        double uPos = bbox.Min.U + c * uDist + shift;
                        if (uPos > bbox.Min.U + 0.001 && uPos < bbox.Max.U - 0.001)
                        {
                            XYZ p1 = face.Evaluate(new UV(uPos, currentVLine));
                            XYZ p2 = face.Evaluate(new UV(uPos, nextVLine));
                            curves.Add(Line.CreateBound(p1, p2));
                        }
                    }
                }
            }

            return curves;
        }
    }
}
