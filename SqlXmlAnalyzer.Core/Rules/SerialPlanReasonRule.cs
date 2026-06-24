using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class SerialPlanReasonRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_014_SERIAL_PLAN_REASON";
        public string Name => "Serial Plan Reason";
        public string Description => "Detects the reason why a plan was executed serially when it could have been parallel.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                // This property is usually on the root QueryPlan node
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                var queryPlan = relOp.Document?.Root?.Element(ns + "BatchSequence")
                                    ?.Element(ns + "Batch")?.Element(ns + "Statements")
                                    ?.Element(ns + "StmtSimple")?.Element(ns + "QueryPlan")
                                ?? relOp.Document?.Root?.Descendants(ns + "QueryPlan").FirstOrDefault();

                if (queryPlan == null) return null;

                var nonParallelReason = queryPlan.Attribute("NonParallelPlanReason")?.Value;

                if (!string.IsNullOrEmpty(nonParallelReason))
                {
                    string chineseReason = nonParallelReason switch
                    {
                        "MaxDOPSetToOne" => "系统或查询级别的 MAXDOP 被设置为 1",
                        "CouldNotGenerateValidParallelPlan" => "优化器未能生成有效的并行计划",
                        "NoParallelPlansInDesktopOrExpressEdition" => "Express/Desktop 版本的 SQL Server 不支持并行计划",
                        "CostNotEnoughForParallelPlan" => "查询开销未达到并行执行的成本阈值 (Cost Threshold for Parallelism)",
                        _ => nonParallelReason
                    };

                    string title = nonParallelReason == "MaxDOPSetToOne" || nonParallelReason == "CostNotEnoughForParallelPlan"
                                   ? "提示: 串行执行计划 (Serial Plan)"
                                   : "降级警告: 串行执行计划 (Serial Plan)";

                    string severity = nonParallelReason == "CouldNotGenerateValidParallelPlan" ? "Warning" : "Info";

                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = severity,
                        Title = title,
                        Message = $"查询以串行模式 (单线程) 执行。\n原因: {chineseReason}\n如果查询开销极大且耗时较长，请检查是否因为特定的 T-SQL 结构（如标量函数、表变量）阻止了并行化，或者检查服务器 MAXDOP 设置。",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"SerialPlanReasonRule failed: {ex.Message}");
            }
            return null;
        }
    }
}
