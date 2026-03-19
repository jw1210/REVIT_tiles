using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace TilePlanner.Core.Utils
{
    // ==========================================
    // [V4.1.21] 全域警告吞噬者與錯誤處理 (Warning Swallowers)
    // ==========================================
    
    /// <summary>
    /// 自動靜默刪除警告，並嘗試解析錯誤 (Miter 專用)
    /// </summary>
    public class AutoDeleteFailureHandler : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            if (failures.Count == 0) return FailureProcessingResult.Continue;
            foreach (FailureMessageAccessor failure in failures)
            {
                if (failure.GetSeverity() == FailureSeverity.Warning) 
                    failuresAccessor.DeleteWarning(failure);
                else if (failure.GetSeverity() == FailureSeverity.Error && failure.HasResolutions()) 
                    failuresAccessor.ResolveFailure(failure);
            }
            return FailureProcessingResult.ProceedWithCommit;
        }
    }

    /// <summary>
    /// 純淨的警告吞噬者，只刪除警告不處理錯誤 (Engine 專用)
    /// </summary>
    public class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor f in failures)
            {
                // 只要是警告 (如：零件未相交)，一律靜默刪除
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(f);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
