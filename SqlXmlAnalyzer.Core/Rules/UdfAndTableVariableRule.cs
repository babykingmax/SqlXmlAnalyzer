using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class UdfAndTableVariableRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_011_UDF_TVF";
        public string Name => "UDF & Table Variable Detection";
        public string Description => "Detects the use of Scalar UDFs, Table-Valued Functions (TVF), or Table Variables which can cause cardinality estimation issues or row-by-row execution.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                var physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "";
                var logicalOp = relOp.Attribute("LogicalOp")?.Value ?? "";

                bool isUdfOrTvf = physicalOp.Contains("Function", StringComparison.OrdinalIgnoreCase) ||
                                  logicalOp.Contains("UDF", StringComparison.OrdinalIgnoreCase) ||
                                  physicalOp.Contains("Table Valued", StringComparison.OrdinalIgnoreCase);

                if (!isUdfOrTvf) return null;

                string estRowsStr = relOp.Attribute("EstimateRows")?.Value ?? "";
                NumericParser.TryParseInvariantDouble(estRowsStr, out double estimatedRows);

                double totalActualRows = 0;
                var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                if (runTimeInfo != null)
                {
                    foreach (var counter in runTimeInfo.Elements(ns + "RunTimeCountersPerThread"))
                    {
                        if (NumericParser.TryParseInvariantDouble(counter.Attribute("ActualRows")?.Value, out double actualRows))
                            totalActualRows += actualRows;
                    }
                }

                string msg = "";
                string severity = "Warning";

                if (totalActualRows > 0 && estimatedRows > 0)
                {
                    double ratio = totalActualRows / estimatedRows;
                    if (ratio > 100 || (estimatedRows == 1 && totalActualRows > 100))
                    {
                        severity = "Critical";
                        msg = $"表变量或 TVF 导致了严重的基数估算失败！优化器预估 {estimatedRows:N0} 行，实际输出 {totalActualRows:N0} 行（偏差 {ratio:F0} 倍）。\n这会彻底带坏后续 JOIN 算子的选择，强烈建议改用 #临时表 (Temp Table) 以获取直方图统计信息支持。";
                    }
                    else
                    {
                        msg = $"检测到使用了表变量、TVF 或 UDF。\n预估行数: {estimatedRows:N0}，实际行数: {totalActualRows:N0}。\n注意：标量 UDF 会导致逐行执行 (Row-by-Row) 且禁用并行计划；表变量缺乏统计信息。";
                    }
                }
                else
                {
                    msg = $"检测到使用了表变量、TVF 或 UDF。\n注意：标量 UDF 会导致逐行执行 (Row-by-Row) 并禁用并行计划；表变量缺乏分布统计信息，若数据量较大建议改用 #临时表。";
                }

                return new AnalysisResult
                {
                    RuleId = this.RuleId,
                    Severity = severity,
                    Title = $"UDF / 表变量性能警告 ({logicalOp})",
                    Message = msg,
                    NodeId = nodeId
                };
            }
            catch (Exception ex)
            {
                Logger.Warning($"UdfAndTableVariableRule failed: {ex.Message}");
            }
            return null;
        }
    }
}
