using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class TableScanRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_021_TABLE_SCAN";
        public string Name => "Table Scan Detection";
        public string Description => "Detects high-cost table scan operations without search predicates.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                var physOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

                if (physOp == "Table Scan" || physOp == "Clustered Index Scan")
                {
                    string seekPred = PlanDiagnosticAnalyzer.ExtractSeekPredicate(relOp, ns);
                    string pred = PlanDiagnosticAnalyzer.ExtractPredicates(relOp, ns);
                    double estRows = PlanDiagnosticAnalyzer.ParseDouble(relOp.Attribute("EstimateRows")?.Value);
                    double tableCard = PlanDiagnosticAnalyzer.ParseDouble(relOp.Attribute("TableCardinality")?.Value);
                    if (tableCard < 1) tableCard = 1;

                    if (string.IsNullOrEmpty(seekPred) && string.IsNullOrEmpty(pred) && estRows > 1000 && (estRows / tableCard) > 0.8)
                    {
                        string objName = PlanDiagnosticAnalyzer.ExtractObjectName(relOp, ns);
                        return new AnalysisResult
                        {
                            RuleId = this.RuleId,
                            Severity = "Warning",
                            Title = "宽表全扫描风险",
                            Message = $"🚫 宽表扫描 Node {nodeId}: 在无任何 Where 过滤谓词（无Seek/Scan过滤条件）的情况下，对大表 {objName} 扫描了超过 80% 记录 (扫描行数 {estRows:F0} / {tableCard:F0})。建议做 limit 限制或添加定位过滤条件。",
                            NodeId = nodeId
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"TableScanRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
