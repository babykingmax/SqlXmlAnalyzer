using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class KeyLookupOpRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_031_KEY_LOOKUP_OP";
        public string Name => "Key/RID Lookup Operator Rule";
        public string Description => "Detects Key/RID lookup operations and suggests covering indexes and avoiding SELECT *.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                string physOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

                if (physOp == "Key Lookup" || physOp == "RID Lookup")
                {
                    string objName = PlanDiagnosticAnalyzer.ExtractObjectName(relOp, ns);
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "键查找与回表开销",
                        Message = $"🔖 Lookup 回表 Node {nodeId}: 对表 {objName} 发生了回表查找（Key Lookup）。说明所检索非聚集索引没有完全覆盖字段。推荐使用覆盖索引。 [DBA 提示] 请同时检查 SQL 语句是否滥用了 SELECT *，若能移除不必要输出的列，可直接免去回表开销。",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"KeyLookupOpRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
