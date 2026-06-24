using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class LargeMemoryGrantRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_017_LARGE_MEMORY_GRANT";
        public string Name => "Large Memory Grant";
        public string Description => "Detects excessive memory grants or tempdb spills.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                var document = relOp.Document;
                if (document == null) return null;

                // 1. TempDB Spill (Critical)
                var spillNode = document.Descendants(ns + "SpillToTempDb").FirstOrDefault();
                if (spillNode != null)
                {
                    int spillLevel = 0;
                    int.TryParse(spillNode.Attribute("SpillLevel")?.Value, out spillLevel);
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Critical",
                        Title = "TempDB 内存溢出 (Spill To TempDb)",
                        Message = $"内存溢出 (Spill Level {spillLevel})：数据写入 TempDB，严重影响性能。\n建议：增加服务器内存或优化查询（如减少不必要排序和哈希运算）。",
                        NodeId = nodeId
                    };
                }

                // 2. Memory Grant ratio (Warning)
                var grantNode = document.Descendants(ns + "MemoryGrantInfo").FirstOrDefault();
                if (grantNode != null)
                {
                    if (long.TryParse(grantNode.Attribute("GrantedMemory")?.Value, out long grantedKB) &&
                        long.TryParse(grantNode.Attribute("MaxUsedMemory")?.Value ?? grantNode.Attribute("UsedMemory")?.Value, out long usedKB))
                    {
                        if (grantedKB > 0)
                        {
                            double ratio = (double)grantedKB / Math.Max(1, usedKB);
                            bool isExcessive = ratio >= 4.0 && grantedKB >= 10 * 1024; // 10 MB

                            if (isExcessive)
                            {
                                return new AnalysisResult
                                {
                                    RuleId = this.RuleId,
                                    Severity = "Warning",
                                    Title = "内存过度分配 (Large Memory Grant)",
                                    Message = $"内存过度分配：申请 {grantedKB:N0} KB，使用 {usedKB:N0} KB，浪费率 {ratio:F1}x。\n建议：检查 TOP / DISTINCT / ORDER BY 是否必要，或拆分查询减少内存需求。",
                                    NodeId = nodeId
                                };
                            }
                        }
                    }
                }


            }
            catch (Exception ex)
            {
                Logger.LogException("LargeMemoryGrantRule failed", ex);
            }
            return null;
        }
    }
}
