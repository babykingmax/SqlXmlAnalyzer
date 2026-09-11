using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class CacheAndRecompileRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_019_CACHE_RECOMPILE";
        public string Name => "Cache & Recompile Detection";
        public string Description => "Detects compile time overhead and full level optimization compile overhead.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            var queryPlan = Services.QueryPlanXml.Find(relOp, ns);
            if (queryPlan == null) return null;

            var messages = new List<string>();

            double compileTime = PlanDiagnosticAnalyzer.ParseDouble(queryPlan.Attribute("CompileTime")?.Value);
            double compileCPU = PlanDiagnosticAnalyzer.ParseDouble(queryPlan.Attribute("CompileCPU")?.Value);
            if (compileTime > 500)
            {
                string cpu = queryPlan.Attribute("CompileCPU") == null ? "N/A" : compileCPU.ToString("F0");
                messages.Add($"编译开销较高：编译时间 {compileTime:F0} 毫秒 (CPU: {cpu} 毫秒)。这是计划记录的编译指标，不能据此认定本次执行缓存未命中或发生重编译；请结合编译频率和运行时采集验证。");
            }

            var stmtSimple = queryPlan.Ancestors(ns + "StmtSimple").FirstOrDefault();
            if (stmtSimple != null)
            {
                string reason = stmtSimple.Attribute("StatementOptmLevel")?.Value ?? "";
                if (reason == "FULL")
                {
                    double cost = PlanDiagnosticAnalyzer.ParseDouble(stmtSimple.Attribute("StatementSubTreeCost")?.Value);
                    if (cost > 50)
                    {
                        messages.Add($"复杂计划编译：优化器采用 FULL 级别优化，计划估算成本为 {cost:F1}。该成本不是编译 CPU 用量；请结合实际编译时间和执行频率评估影响。");
                    }
                }
            }

            if (messages.Any())
            {
                return new AnalysisResult
                {
                    RuleId = this.RuleId,
                    Severity = "Warning",
                    Title = "缓存命中与重编译开销",
                    Message = string.Join("|||", messages),
                    NodeId = "0"
                };
            }

            return null;
        }
    }
}
