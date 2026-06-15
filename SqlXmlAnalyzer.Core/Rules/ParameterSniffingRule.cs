using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ParameterSniffingRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_003_PARAM_SNIFFING";
        public string Name => "Parameter Sniffing Detection";
        public string Description => "Detects parameter sniffing by comparing compiled and runtime parameter values.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                
                // Only execute this rule on the Root node (NodeId = 0) to avoid duplicate warnings
                if (nodeId != "0" && nodeId != "1") return null;

                var queryPlan = relOp.Document?.Descendants(ns + "QueryPlan").FirstOrDefault();
                if (queryPlan != null)
                {
                    // Check for OPTIMIZE FOR UNKNOWN
                    if (queryPlan.ToString().Contains("OPTIMIZE FOR UNKNOWN", StringComparison.OrdinalIgnoreCase))
                    {
                        return new AnalysisResult
                        {
                            RuleId = "RULE_003_OPTIMIZE_FOR_UNKNOWN",
                            Severity = "Info",
                            Title = "提示: OPTIMIZE FOR UNKNOWN",
                            Message = "检测到查询使用了 OPTION (OPTIMIZE FOR UNKNOWN)。优化器将使用统计信息的平均密度进行预估，而不是特定参数的值。这有助于缓解参数嗅探，但可能导致所有参数均获得次优计划。",
                            NodeId = nodeId
                        };
                    }
                }

                var paramList = relOp.Document?.Descendants(ns + "ParameterList").Descendants(ns + "ColumnReference");
                if (paramList == null) return null;

                var sniffedParams = new List<string>();

                foreach (var p in paramList)
                {
                    string col = p.Attribute("Column")?.Value ?? "";
                    string? comp = p.Attribute("ParameterCompiledValue")?.Value;
                    string? run = p.Attribute("ParameterRuntimeValue")?.Value;

                    if (!string.IsNullOrEmpty(comp) && !string.IsNullOrEmpty(run) && comp != run)
                    {
                        sniffedParams.Add($"{col} (编译值: {comp}, 运行值: {run})");
                    }
                }

                if (sniffedParams.Any())
                {
                    // Check for row estimate deviation on root node
                    string estRowsStr = relOp.Attribute("EstimateRows")?.Value ?? "1";
                    NumericParser.TryParseInvariantDouble(estRowsStr, out double estimateRows);
                    
                    double actualRows = 0;
                    var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                    if (runTimeInfo != null)
                    {
                        foreach (var counter in runTimeInfo.Elements(ns + "RunTimeCountersPerThread"))
                        {
                            if (NumericParser.TryParseInvariantDouble(counter.Attribute("ActualRows")?.Value, out double act))
                                actualRows += act;
                        }
                    }

                    double ratio = estimateRows > 0 ? actualRows / estimateRows : 1;
                    if (actualRows < estimateRows && actualRows > 0) ratio = estimateRows / actualRows;

                    string severity = "Warning";
                    if (ratio >= 100 && actualRows > 1000) severity = "Critical";
                    else if (ratio >= 10) severity = "Warning";
                    else severity = "Info";

                    var statsList = relOp.Document != null ? Parsers.StatisticsUsageParser.Parse(relOp.Document, ns) : new List<Models.StatisticsInfo>();
                    var staleStats = statsList.Where(s => s.IsStale || s.ModificationCount > 1000).ToList();
                    string statsWarning = "";

                    if (staleStats.Any())
                    {
                        severity = "Critical"; // Elevate to Critical if we have parameter sniffing combined with stale stats
                        statsWarning = "\n⚠️ 伴随的统计信息风险（Stale Statistics）：\n" +
                                       string.Join("\n", staleStats.Select(s => $"   • [{s.Table}] (统计项: {s.Statistics}) 更新账龄: {s.AgeInDays}天, 修改量: {s.ModificationCount:N0}")) +
                                       "\n👉 建议优先执行: UPDATE STATISTICS 对涉及的表进行更新，防止由于过时统计导致基数预估失准。\n";
                    }

                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = severity,
                        Title = "参数嗅探风险 (Parameter Sniffing)",
                        Message = $"检测到编译期参数与运行时参数值不一致：\n" + 
                                  string.Join("\n", sniffedParams) + 
                                  $"\n当前根节点预估与实际行数偏差比例: {ratio:F1}x。\n" +
                                  statsWarning +
                                  "建议方案：\n1. 使用局部变量阻断嗅探: DECLARE @LocalParam = @Parameter\n2. 添加 OPTION (RECOMPILE) 或 OPTION (OPTIMIZE FOR UNKNOWN)\n3. SQL Server 2022+ 评估参数敏感计划优化 (PSP)",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("ParameterSniffingRule failed", ex);
            }

            return null;
        }
    }
}
