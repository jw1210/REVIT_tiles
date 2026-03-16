using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace TilePlanner.Core
{
    public class GridConstraintManager
    {
        private readonly Document _doc;

        public GridConstraintManager(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        // [V3.5 終極修復] 將參考平面打包為群組，強制達成整體連動
        public void GroupPlanes(List<ElementId> planeIds, string groupNamePrefix, string hostId)
        {
            if (planeIds == null || planeIds.Count < 2) return;

            try
            {
                Group group = _doc.Create.NewGroup(planeIds);
                if (group != null)
                {
                    // 加上短 Guid 防止使用者多次重繪時，產生「群組名稱已存在」的報錯
                    group.GroupType.Name = $"{groupNamePrefix}_{hostId}_{Guid.NewGuid().ToString().Substring(0, 5)}";
                }
            }
            catch (Exception) 
            { 
                /* 忽略極端情況下的群組建立失敗 */ 
            }
        }
    }
}
