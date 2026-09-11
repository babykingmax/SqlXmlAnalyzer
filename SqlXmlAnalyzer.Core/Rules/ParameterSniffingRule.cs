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
            var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";

            var queryPlan = Services.QueryPlanXml.Find(relOp, ns);
            var statement = queryPlan == null ? null : StatementSqlAnalysis.Parse(queryPlan, ns);
            var unknown = statement == null ? null : StatementSqlAnalysis.GetOptimizeForUnknown(statement);
            AnalysisResult HintResult() => new()
            {
                RuleId = "RULE_003_OPTIMIZE_FOR_UNKNOWN",
                Severity = "Info",
                Title = "提示: OPTIMIZE FOR UNKNOWN",
                Message = "StatementText 中包含 OPTIMIZE FOR UNKNOWN 或针对特定参数的 UNKNOWN 提示。优化器对受影响参数使用统计信息分布进行估算，而不采用本次参数值；请结合实际基数和参数分布验证计划表现。",
                NodeId = nodeId
            };
            if (unknown?.AllParameters == true) return HintResult();

            var paramList = queryPlan?.Elements(ns + "ParameterList").Elements(ns + "ColumnReference");
            if (paramList == null) return unknown?.Parameters.Count > 0 ? HintResult() : null;

            var sniffedParams = new List<string>();

            foreach (var p in paramList)
            {
                string col = p.Attribute("Column")?.Value ?? "";
                // A per-parameter hint must not suppress evidence for other parameters.
                if (unknown?.Parameters.Contains(col) == true) continue;
                string? comp = p.Attribute("ParameterCompiledValue")?.Value;
                string? run = p.Attribute("ParameterRuntimeValue")?.Value;

                if (!string.IsNullOrEmpty(comp) && !string.IsNullOrEmpty(run) && comp != run)
                {
                    sniffedParams.Add($"{col} (编译值: {comp}, 运行值: {run})");
                }
            }

            if (sniffedParams.Any())
            {
                var facts = Services.PlanOperatorFactsService.Get(relOp, ns);
                double estimateRows = facts.EstimatedRows.Value ?? 0;
                double actualRows = (double)(facts.RowsPerExecution.Value ?? 0);
                bool hasComparableRows = facts.EstimatedRows.IsAvailable && facts.RowsPerExecution.IsAvailable;
                double ratio = estimateRows > 0 ? actualRows / estimateRows : 1;
                if (actualRows < estimateRows && actualRows > 0) ratio = estimateRows / actualRows;

                string severity = "Warning";
                if (ratio >= 100 && actualRows > 1000) severity = "Critical";
                else if (ratio >= 10) severity = "Warning";
                else severity = "Info";

                var statsList = queryPlan != null ? Parsers.StatisticsUsageParser.ParseQueryPlan(queryPlan, ns) : new List<Models.StatisticsInfo>();
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
                              (hasComparableRows ? $"\n当前根节点每次执行行数偏差比例: {ratio:F1}x。\n" : "\n每次执行行数偏差比例: N/A（缺少可比指标）。\n") +
                              statsWarning +
                              "建议方案：\n1. 使用局部变量阻断嗅探: DECLARE @LocalParam = @Parameter\n2. 添加 OPTION (RECOMPILE) 或 OPTION (OPTIMIZE FOR UNKNOWN)\n3. SQL Server 2022+ 评估参数敏感计划优化 (PSP)",
                    NodeId = nodeId
                };
            }

            return unknown?.Parameters.Count > 0 ? HintResult() : null;
        }
    }
}
