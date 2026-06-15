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
            if (relOp.Attribute("NodeId")?.Value != "0") return null;

            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var statsList = SqlXmlAnalyzer.Core.Parsers.StatisticsUsageParser.Parse(doc, ns);
                if (statsList.Count > 0)
                {
                    var sbStats = new StringBuilder();
                    sbStats.AppendLine("📊 优化器统计信息使用状态 (OptimizerStatsUsage):");
                    foreach (var stat in statsList)
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
                        sbStats.AppendLine($"   • {statusIcon} [{stat.Database}].[{stat.Schema}].[{stat.Table}] (统计项: {stat.Statistics}){warningDetails}");

                        if (!string.IsNullOrEmpty(warningDetails))
                        {
                            sbStats.AppendLine($"     👉 优化建议: UPDATE STATISTICS [{stat.Database}].[{stat.Schema}].[{stat.Table}]({stat.Statistics}) WITH FULLSCAN;");
                        }
                    }

                    // Trim trailing newlines
                    string messageStr = sbStats.ToString().TrimEnd('\r', '\n');

                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "统计信息使用状态",
                        Message = messageStr,
                        NodeId = "0"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"StatsUsageRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
