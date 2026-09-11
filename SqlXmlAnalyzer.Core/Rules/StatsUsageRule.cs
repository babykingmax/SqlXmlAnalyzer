using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class StatsUsageRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_028_STATS_USAGE";
        public string Name => "Optimizer Statistics Usage Detection";
        public string Description => "Analyzes optimizer statistics usage status and warns of stale or low-sampling statistics.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            var queryPlan = Services.QueryPlanXml.Find(relOp, ns);
            if (queryPlan == null) return null;

            var statsList = SqlXmlAnalyzer.Core.Parsers.StatisticsUsageParser.ParseQueryPlan(queryPlan, ns);
            var riskyStats = statsList
                .Where(stat => stat.Severity != "Info")
                .ToList();

            if (riskyStats.Count > 0)
            {
                var sbStats = new StringBuilder();
                sbStats.AppendLine("📊 优化器统计信息使用状态 (OptimizerStatsUsage):");
                foreach (var stat in riskyStats)
                {
                    string warningDetails = "";
                    if (stat.IsStale)
                    {
                        warningDetails += $" ⚠️ 已过时 (更新账龄: {stat.AgeInDays}天)";
                    }
                    if (stat.ModificationCount > 1000)
                    {
                        warningDetails += $" ⚠️ 频繁变动 (修改次数: {stat.ModificationCount:N0})";
                    }
                    if (stat.IsLowSampling)
                    {
                        warningDetails += $" ⚠️ 低采样率 (采样率: {stat.SamplingPercent:F1}%)";
                    }

                    string statusIcon = string.IsNullOrEmpty(warningDetails) ? "✅" : "⚠️";
                    sbStats.AppendLine($"   • {statusIcon} {stat.Database}.{stat.Schema}.{stat.Table} (统计项: {stat.Statistics}){warningDetails}");

                    if (!string.IsNullOrEmpty(warningDetails))
                    {
                        try
                        {
                            sbStats.AppendLine($"     👉 审核后自行执行: {Services.StatisticsCommandBuilder.BuildUpdateStatistics(stat)}");
                        }
                        catch (ArgumentException)
                        {
                            sbStats.AppendLine("     统计信息对象标识缺失或无效，未生成 SQL；请先核对数据库、架构、表与统计信息名称。");
                        }
                    }
                }

                // Trim trailing newlines
                string messageStr = sbStats.ToString().TrimEnd('\r', '\n');

                return new AnalysisResult
                {
                    RuleId = this.RuleId,
                    Severity = riskyStats.Any(stat => stat.Severity == "Critical") ? "Critical" : "Warning",
                    Title = "统计信息使用状态",
                    Message = messageStr,
                    NodeId = "0"
                };
            }

            return null;
        }
    }
}
