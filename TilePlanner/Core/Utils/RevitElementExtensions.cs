using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core.Utils
{
    /// <summary>
    /// 包含最底層的核心 Revit 幾何屬性萃取 (GetCentroid, GetSolid 等)
    /// 保留 V4.1.21 原始邏輯
    /// </summary>
    public static class RevitElementExtensions
    {
        public static XYZ GetCentroid(this Element e)
        {
            BoundingBoxXYZ b = e.get_BoundingBox(null);
            return b != null ? (b.Min + b.Max) / 2.0 : XYZ.Zero;
        }

        public static Solid GetSolid(this Part p)
        {
            return p.get_Geometry(new Options()).OfType<Solid>().FirstOrDefault(s => s.Volume > 0);
        }

        public static XYZ GetDominantFaceNormal(this Part p)
        {
            Solid s = p.GetSolid();
            if (s == null) return XYZ.BasisZ;
            return s.Faces.OfType<PlanarFace>().OrderByDescending(f => f.Area).First().FaceNormal;
        }

        public static Wall GetHostWall(this Part p)
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
    }
}
