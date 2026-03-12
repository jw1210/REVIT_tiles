using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TilePlanner.Commands
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class DumpRefPlaneParams : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            var rp = new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).FirstElement() as ReferencePlane;
            if (rp != null)
            {
                string s = string.Empty;
                foreach (Parameter p in rp.Parameters)
                {
                    s += p.Definition.Name;
                    if (p.Definition is InternalDefinition id) s += "" ("" + id.BuiltInParameter.ToString() + "")"";
                    s += ""\n"";
                }
                TaskDialog.Show(""Params"", s);
            }
            return Result.Succeeded;
        }
    }
}
