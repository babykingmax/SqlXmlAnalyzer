using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class NestedLoopsRunningTotalRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_023_RUNNING_TOTAL_PATTERN";
        public string Name => "Nested Loops Inequality Pattern";
        public string Description => "Detects Nested Loops with Inequality Seek Predicates indicative of a running total pattern.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var statement = relOp.Ancestors(ns + "StmtSimple").FirstOrDefault();
                var relOps = statement != null
                    ? statement.Descendants(ns + "RelOp").ToList()
                    : doc.Descendants(ns + "RelOp").ToList();
                var nestedLoops = relOps.Where(r => r != null && r.Attribute("PhysicalOp")?.Value == "Nested Loops").ToList();

                if (nestedLoops.Count >= 2)
                {
                    foreach (var nl in nestedLoops)
                    {
                        if (nl == null) continue;
                        var childOps = PlanDiagnosticAnalyzer.GetDirectChildRelOps(nl, ns);
                        var innerSeek = childOps.FirstOrDefault(c => c != null && c.Attribute("PhysicalOp")?.Value.Contains("Seek") == true);
                        if (innerSeek != null)
                        {
                            string sp = PlanDiagnosticAnalyzer.ExtractSeekPredicate(innerSeek, ns);
                            bool hasInequality = sp.Contains("<") || sp.Contains(">");
                            if (hasInequality)
                            {
                                string nlNodeId = nl.Attribute("NodeId")?.Value ?? "?";
                                return new AnalysisResult
                                {
                                    RuleId = this.RuleId,
                                    Severity = "Critical",
                                    Title = "嵌套循环与不等式查找",
                                    Message = $"🚨 **[隐蔽风暴] 检测到嵌套循环 + 不等式查找 模式 (在 Node {nlNodeId})**\n   这代表 SQL Server 在处理一个典型的“累积和(Running Total)”或“时间区间重叠”反模式，可能引发 CPU O(N²) 暴增。\n   👉 **优化处方**: 强烈建议重写为 Window 窗口函数 (SUM() OVER (ORDER BY...)) 或修正不等式关联。",
                                    NodeId = nlNodeId
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"NestedLoopsRunningTotalRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
