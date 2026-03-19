using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.Attributes;
using TilePlanner.Core.Services;

namespace TilePlanner.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AutoMiterJoinCommandV4115 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (!TilePlanner.Security.LicenseManager.Validate()) return Result.Failed;

            try 
            {
                // 1. 選取零件
                IList<Reference> refsA = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), "選取 A 側磁磚 (按 Enter 結束)");
                IList<Reference> refsB = uidoc.Selection.PickObjects(ObjectType.Element, new PartOnlyFilter(), "選取 B 側磁磚 (按 Enter 結束)");
                if (refsA == null || refsB == null || refsA.Count == 0 || refsB.Count == 0) return Result.Cancelled;

                // 2. 委派服務層處理斜切邏輯 (維持 100% V4.1.21 數學相容)
                return MiterJoinService.ExecuteMiterJoin(doc, refsA, refsB);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled; // 攔截使用者按 ESC 取消的例外
            }
        }
    }

    public class PartOnlyFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Part;
        public bool AllowReference(Reference r, XYZ p) => true;
    }
}
