using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class RowEstimateMismatchRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_004_ESTIMATE_MISMATCH";
        public string Name => "Row Estimate Mismatch (10x+ / 100x+)";
        public string Description => "Detects if actual rows deviate significantly from estimated rows.";

        private const double MISMATCH_THRESHOLD = 10.0;
        private const double CRITICAL_MISMATCH_THRESHOLD = 100.0;
        private const double MIN_ACTUAL_ROWS = 100.0;

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                var physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "";
                
                string estRowsStr = relOp.Attribute("EstimateRows")?.Value ?? "";
                if (!double.TryParse(estRowsStr, out double estimatedRows))
                    return null;

                // Find RunTimeInformation -> RunTimeCountersPerThread
                var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                if (runTimeInfo == null) return null;

                double totalActualRows = 0;
                double maxExecutions = 1;

                foreach (var counter in runTimeInfo.Elements(ns + "RunTimeCountersPerThread"))
                {
                    if (double.TryParse(counter.Attribute("ActualRows")?.Value, out double actualRows))
                        totalActualRows += actualRows;
                    if (double.TryParse(counter.Attribute("ActualExecutions")?.Value, out double execs))
                        maxExecutions = Math.Max(maxExecutions, execs);
                }

                // Remove the old Zero-Row actuals logic here, as we moved it to ZeroRowActualsRule.cs
                
                // Check CardinalityEstimationModelVersion on Statement
                var stmtSimple = relOp.Document?.Descendants(ns + "StmtSimple").FirstOrDefault();
                string ceVersionStr = stmtSimple?.Attribute("CardinalityEstimationModelVersion")?.Value ?? "";
                if (int.TryParse(ceVersionStr, out int ceVersion) && ceVersion < 120 && nodeId == "0")
                {
                    // For root node, we can optionally warn about old CE
                }

                double actualRowsPerExec = totalActualRows / Math.Max(1.0, maxExecutions);

                if (actualRowsPerExec <= 100 && estimatedRows >= 1000)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Critical",
                        Title = "基数估计严重偏差 (预估极高, 实际极低)",
                        Message = $"优化器预估会返回 {estimatedRows:N0} 行，但实际只返回了 {actualRowsPerExec:N0} 行。这种过高的预估会导致优化器错选 Hash Join 或请求极大的内存（导致内存浪费）。建议检查过滤条件或更新统计信息。",
                        NodeId = nodeId
                    };
                }

                if (actualRowsPerExec < MIN_ACTUAL_ROWS && estimatedRows < MIN_ACTUAL_ROWS)
                    return null;

                double ratio = 1.0;
                if (actualRowsPerExec > estimatedRows)
                {
                    ratio = actualRowsPerExec / Math.Max(1.0, estimatedRows);
                }
                else
                {
                    ratio = estimatedRows / Math.Max(1.0, actualRowsPerExec);
                }

                if (ratio >= CRITICAL_MISMATCH_THRESHOLD)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Critical",
                        Title = "基数估计严重偏差 (偏差 > 100倍)",
                        Message = $"预估返回 {estimatedRows:N0} 行，每次执行实际返回 {actualRowsPerExec:N0} 行。偏差比例高达 {ratio:F0} 倍！统计信息已严重过时，极易导致糟糕的执行计划。",
                        NodeId = nodeId
                    };
                }
                else if (ratio >= MISMATCH_THRESHOLD)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "基数估计偏差 (偏差 > 10倍)",
                        Message = $"预估返回 {estimatedRows:N0} 行，每次执行实际返回 {actualRowsPerExec:N0} 行。偏差比例 {ratio:F1} 倍。建议更新统计信息 (UPDATE STATISTICS)。",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"RowEstimateMismatchRule failed: {ex.Message}");
            }
            return null;
        }
    }
}
