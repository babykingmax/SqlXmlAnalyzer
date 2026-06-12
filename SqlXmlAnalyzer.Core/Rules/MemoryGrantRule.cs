using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class MemoryGrantRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_005_MEMORY_GRANT";
        public string Name => "Memory Grant vs Used Ratio / Large Memory Grant";
        public string Description => "Detects if a query requested significantly more memory than it actually used.";

        private const double GRANT_USED_RATIO_THRESHOLD = 4.0;
        private const double MIN_GRANT_USED_KB = 10.0 * 1024.0; // 10MB in KB

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";

                // MemoryGrants are typically found on the root node (NodeId = 0) inside QueryPlan
                // We should check if the current relOp is the root node or contains MemoryGrantInfo
                var memoryGrantInfo = relOp.Element(ns + "MemoryGrantInfo");
                if (memoryGrantInfo == null)
                {
                    // Sometimes it's inside QueryPlan -> MemoryGrantInfo
                    memoryGrantInfo = relOp.Document?.Root?.Element(ns + "QueryPlan")?.Element(ns + "MemoryGrantInfo");
                    // Only process this once for the root node to avoid duplicates
                    if (nodeId != "0" && nodeId != "1") return null; 
                }

                if (memoryGrantInfo == null) return null;

                string grantedStr = memoryGrantInfo.Attribute("GrantedMemory")?.Value ?? "";
                string usedStr = memoryGrantInfo.Attribute("MaxUsedMemory")?.Value ?? "";

                if (!double.TryParse(grantedStr, out double grantedMemoryKB) || 
                    !double.TryParse(usedStr, out double usedMemoryKB))
                {
                    return null;
                }

                if (grantedMemoryKB <= 0) return null;

                double ratio = grantedMemoryKB / Math.Max(1.0, usedMemoryKB);
                bool isExcessive = ratio >= GRANT_USED_RATIO_THRESHOLD && grantedMemoryKB >= MIN_GRANT_USED_KB;

                if (isExcessive)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "内存过度授权 (Excessive Memory Grant)",
                        Message = $"申请了 {grantedMemoryKB:N0} KB 内存，但实际只使用了 {usedMemoryKB:N0} KB。\n浪费比例高达: {ratio:F1} 倍。\n建议检查 TOP/DISTINCT/ORDER BY 等操作，避免不必要的内存分配。",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"MemoryGrantRule failed: {ex.Message}");
            }
            return null;
        }
    }
}
