using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TilePlanner.Core;

namespace TilePlanner.Commands
{
    /// <summary>
    /// 移除磁磚計畫指令
    /// 支援帷幕牆、帷幕系統、帷幕屋頂
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveTilePlanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                Reference elemRef;
                try
                {
                    elemRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new PartSelectionFilter(), // 這裡改用我們自定義的 Part 過濾器
                        "請選取要移除磁磚計畫的實體零件 (Part)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                Element selectedElement = doc.GetElement(elemRef.ElementId);
                
                if (!(selectedElement is Part part))
                {
                    TaskDialog.Show("磁磚計畫", "選取的元素不是零件。");
                    return Result.Failed;
                }

                // 1. 尋找原宿主物件 (Host Element)
                ElementId hostId = ElementId.InvalidElementId;
                var sourceIds = part.GetSourceElementIds();
                if (sourceIds != null && sourceIds.Count > 0)
                {
                    // 取得最源頭的宿主 Element (通常是牆壁或樓板)
                    hostId = sourceIds.First().HostElementId;
                }

                if (hostId == ElementId.InvalidElementId)
                {
                    TaskDialog.Show("磁磚計畫", "無法找到此零件的原始宿主牆或樓板。");
                    return Result.Failed;
                }

                Element hostElement = doc.GetElement(hostId);

                using (Transaction trans = new Transaction(doc, "移除磁磚計畫"))
                {
                    trans.Start();

                    try
                    {
                        // 2. 刪除所有與該 Host 相關的 PartMaker 與零件，還原為單一宿主物件
                        ICollection<ElementId> allAssociatedParts = PartUtils.GetAssociatedParts(doc, hostId, true, true);
                        HashSet<ElementId> makersToDelete = new HashSet<ElementId>();
                        foreach (ElementId pId in allAssociatedParts)
                        {
                            PartMaker pm = PartUtils.GetAssociatedPartMaker(doc, pId);
                            if (pm != null)
                            {
                                makersToDelete.Add(pm.Id);
                            }
                        }

                        if (makersToDelete.Count > 0)
                        {
                            // 先刪除 PartMaker（移除分割關係）
                            doc.Delete(makersToDelete.ToList());
                        }

                        if (allAssociatedParts != null && allAssociatedParts.Count > 0)
                        {
                            // 再刪除所有零件，讓牆 / 樓板回到未分割狀態
                            doc.Delete(allAssociatedParts.ToList());
                        }

                        // 3. 清理依附於此 Host 的參照平面 (Reference Planes) 與群組
                        string suffix = $"_{hostId.IntegerValue}";
                        
                        // 刪除參照平面
                        var refPlanes = new FilteredElementCollector(doc)
                            .OfClass(typeof(ReferencePlane))
                            .WhereElementIsNotElementType()
                            .Cast<ReferencePlane>()
                            .Where(rp => rp.Name != null && rp.Name.EndsWith(suffix))
                            .Select(rp => rp.Id)
                            .ToList();

                        if (refPlanes.Count > 0) doc.Delete(refPlanes);

                        // 刪除群組定義 (GroupType) 與其實例 (Group)
                        var groups = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_IOSModelGroups)
                            .WhereElementIsNotElementType()
                            .Cast<Group>()
                            .Where(g => g.GroupType != null && g.GroupType.Name.Contains($"TileGrid_{suffix}"))
                            .Select(g => g.Id)
                            .ToList();
                            
                        if (groups.Count > 0) doc.Delete(groups);

                        var groupTypes = new FilteredElementCollector(doc)
                            .OfClass(typeof(GroupType))
                            .WhereElementIsNotElementType()
                            .Cast<GroupType>()
                            .Where(g => g.Name != null && g.Name.Contains($"TileGrid_{suffix}"))
                            .Select(g => g.Id)
                            .ToList();

                        if (groupTypes.Count > 0) doc.Delete(groupTypes);

                        // 4. 清除掛載在 Host 上的設定資料
                        if (hostElement != null)
                        {
                            TileDataManager.RemoveTileConfig(hostElement);
                        }

                        trans.Commit();
                        TaskDialog.Show("磁磚計畫", "已成功移除磁磚分割，零件已還原原本狀態。");
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        message = $"移除失敗：{ex.Message}";
                        TaskDialog.Show("磁磚計畫 - 錯誤", message);
                        return Result.Failed;
                    }
                }
            }
            catch (Exception ex)
            {
                message = $"執行錯誤：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}
