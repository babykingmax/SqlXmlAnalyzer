using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class OptimizerAbortRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_018_OPTIMIZER_ABORT";
        public string Name => "Optimizer Early Abort Detection";
        public string Description => "Detects optimizer early abort reasons.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var statement = relOp.Ancestors(ns + "StmtSimple").FirstOrDefault()
                    ?? doc.Descendants(ns + "StmtSimple").FirstOrDefault();
                if (statement == null) return null;
                var stmts = new[] { statement };
                var messages = new List<string>();

                foreach (var stmt in stmts)
                {
                    if (stmt == null) continue;
                    string? abortReason = stmt.Attribute("StatementOptmEarlyAbortReason")?.Value;
                    if (!string.IsNullOrEmpty(abortReason) && abortReason != "GoodEnoughPlanFound")
                    {
                        messages.Add($"🚨 SQL 优化器因 [{abortReason}] 提前中止，未能生成最优计划。这代表计划极其复杂或相关统计信息缺失。");
                    }
                }

                if (messages.Any())
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Critical",
                        Title = "优化器提前中止",
                        Message = string.Join("|||", messages),
                        NodeId = "0"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"OptimizerAbortRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
