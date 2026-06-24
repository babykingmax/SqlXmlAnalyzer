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
            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var messages = new List<string>();

                var queryTimeStats = doc.Descendants(ns + "QueryTimeStats").FirstOrDefault();
                if (queryTimeStats != null)
                {
                    double compileTime = PlanDiagnosticAnalyzer.ParseDouble(queryTimeStats.Attribute("CompileTime")?.Value);
                    double compileCPU = PlanDiagnosticAnalyzer.ParseDouble(queryTimeStats.Attribute("CompileCPU")?.Value);
                    if (compileTime > 500)
                    {
                        messages.Add($"♻️ 重编译高开销: 编译时间 {compileTime:F0} 毫秒 (CPU: {compileCPU:F0} 毫秒)。这表明查询未能命中计划缓存 (Cache Miss) 或发生了重编译 (Recompile)，建议检查统计信息更新频率或使用参数化查询。");
                    }
                }

                var stmtSimple = doc.Descendants(ns + "StmtSimple").FirstOrDefault();
                if (stmtSimple != null)
                {
                    string reason = stmtSimple.Attribute("StatementOptmLevel")?.Value ?? "";
                    if (reason == "FULL")
                    {
                        double cost = PlanDiagnosticAnalyzer.ParseDouble(stmtSimple.Attribute("StatementSubTreeCost")?.Value);
                        if (cost > 50)
                        {
                            messages.Add($"⚠️ 复杂计划编译: 优化器进行了 FULL 级别的深度编译，计划预估开销高达 {cost:F1}。如果此查询高频执行，CPU 会被彻底耗尽。");
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
            }
            catch (Exception ex)
            {
                Logger.Warning($"CacheAndRecompileRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
