using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class SpillDetectionRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_008_SPILL_DETECTION";
        public string Name => "Spill to TempDB (Hash/Sort/Exchange)";
        public string Description => "Detects operations that spilled to TempDB due to insufficient memory grants, categorized by severity.";

        private const int MIN_SPILL_LEVEL_WARNING = 1;
        private const int MODERATE_SPILL_LEVEL = 3;
        private const int SEVERE_SPILL_LEVEL = 5;

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                var physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown";

                // Look for SpillToTempDb warnings inside the Warnings node of this RelOp
                // The XML structure usually is: RelOp -> Warnings -> SpillToTempDb
                var warnings = relOp.Element(ns + "Warnings");
                if (warnings == null) return null;

                // Check Hash, Sort, or Exchange spills
                var spillNode = warnings.Element(ns + "SpillToTempDb") ??
                                warnings.Element(ns + "SortSpillDetails") ??
                                warnings.Element(ns + "HashSpillDetails");

                if (spillNode == null) return null;

                // Try to extract the SpillLevel
                int spillLevel = 1; // Default to 1 if not explicitly specified
                string? spillLevelAttr = spillNode.Attribute("SpillLevel")?.Value;
                if (!string.IsNullOrEmpty(spillLevelAttr))
                {
                    int.TryParse(spillLevelAttr, out spillLevel);
                }

                // Try to extract SpilledPages if available
                string? spilledPagesAttr = spillNode.Attribute("SpilledPages")?.Value;
                string spilledPagesMsg = !string.IsNullOrEmpty(spilledPagesAttr) ? $" (溢出页数: {spilledPagesAttr})" : "";

                if (spillLevel >= SEVERE_SPILL_LEVEL)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Critical",
                        Title = $"严重 TempDB 溢出 ({physicalOp})",
                        Message = $"溢出级别 {spillLevel}{spilledPagesMsg}。\n大量数据被写入 TempDB 磁盘，严重拖慢性能！\n强烈建议：优化查询(例如创建合适的索引)以减少内存需求，或者增加系统分配的内存 (Memory Grant)。",
                        NodeId = nodeId
                    };
                }
                else if (spillLevel >= MODERATE_SPILL_LEVEL)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = $"中度 TempDB 溢出 ({physicalOp})",
                        Message = $"溢出级别 {spillLevel}{spilledPagesMsg}。\n数据溢出写入了 TempDB 磁盘。\n建议：检查内存授权是否被其他并发查询限制，尝试优化操作符。",
                        NodeId = nodeId
                    };
                }
                else if (spillLevel >= MIN_SPILL_LEVEL_WARNING)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Info",
                        Title = $"轻微 TempDB 溢出 ({physicalOp})",
                        Message = $"溢出级别 {spillLevel}{spilledPagesMsg}。\n有少量数据溢出到了 TempDB。\n建议：关注此查询的内存使用情况。",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"SpillDetectionRule failed: {ex.Message}");
            }
            return null;
        }
    }
}
