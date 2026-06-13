using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ZeroRowActualsRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_016_ZERO_ROW_ACTUALS";
        public string Name => "Zero-Row Actuals";
        public string Description => "Detects when a node estimated significant rows but actually returned zero.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                
                string estRowsStr = relOp.Attribute("EstimateRows")?.Value ?? "";
                if (!NumericParser.TryParseInvariantDouble(estRowsStr, out double estimatedRows))
                    return null;

                var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                if (runTimeInfo == null) return null;

                double totalActualRows = 0;

                foreach (var counter in runTimeInfo.Elements(ns + "RunTimeCountersPerThread"))
                {
                    if (NumericParser.TryParseInvariantDouble(counter.Attribute("ActualRows")?.Value, out double actualRows))
                        totalActualRows += actualRows;
                }

                if (estimatedRows >= 100 && totalActualRows == 0)
                {
                    var stmtSimple = relOp.Document?.Descendants(ns + "StmtSimple").FirstOrDefault();
                    string statementText = stmtSimple?.Attribute("StatementText")?.Value ?? "";
                    string truncatedSql = statementText.Length > 200 ? statementText.Substring(0, 200) + "..." : statementText;

                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "实际零行返回 (Zero-Row Actuals)",
                        Message = $"优化器预估会返回 {estimatedRows:N0} 行，但实际执行时返回了 0 行。\n这可能是由于统计信息严重过时，或者是由于谓词逻辑错误（如互斥条件导致永远为假）。\n\n涉及 SQL：{truncatedSql}\n建议：更新统计信息或检查 WHERE 条件是否过于严格。",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("ZeroRowActualsRule failed", ex);
            }
            return null;
        }
    }
}
