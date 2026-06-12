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
                if (!double.TryParse(estRowsStr, out double estimatedRows))
                    return null;

                var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                if (runTimeInfo == null) return null;

                double totalActualRows = 0;

                foreach (var counter in runTimeInfo.Elements(ns + "RunTimeCountersPerThread"))
                {
                    if (double.TryParse(counter.Attribute("ActualRows")?.Value, out double actualRows))
                        totalActualRows += actualRows;
                }

                if (estimatedRows >= 100 && totalActualRows == 0)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "实际零行返回 (Zero-Row Actuals)",
                        Message = $"优化器预估会返回 {estimatedRows:N0} 行，但实际执行时返回了 0 行。\n这可能是由于统计信息严重过时，或者是由于谓词逻辑错误（如互斥条件导致永远为假）。\n建议执行 UPDATE STATISTICS 或检查 WHERE 逻辑。",
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
