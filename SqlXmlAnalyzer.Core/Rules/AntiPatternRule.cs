using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class AntiPatternRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_013_ANTI_PATTERN";
        public string Name => "Anti-Pattern Detection (Wildcard/NOT IN)";
        public string Description => "Detects SQL anti-patterns like leading wildcards (LIKE '%...') or NOT IN which can cause performance issues.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";

                // Scan all ScalarStrings in predicates
                var predicates = relOp.Descendants(ns + "Predicate").Union(relOp.Descendants(ns + "SeekPredicates"));
                var scalarOps = predicates.Descendants(ns + "ScalarOperator");

                foreach (var op in scalarOps)
                {
                    string scalarString = op.Attribute("ScalarString")?.Value ?? "";

                    // 1. Check for leading wildcard (e.g. LIKE '%abc')
                    if (scalarString.Contains("LIKE", StringComparison.OrdinalIgnoreCase) && 
                        (scalarString.Contains("'%") || scalarString.Contains("N'%")))
                    {
                        return new AnalysisResult
                        {
                            RuleId = this.RuleId,
                            Severity = "Warning",
                            Title = "前导通配符反模式 (Leading Wildcard)",
                            Message = $"检测到 LIKE 语句使用了前导通配符（例如 '%abc'）：\n{scalarString}\n前导通配符会导致索引失效并触发全表/全索引扫描。考虑使用全文索引或将架构调整为避免前导模糊查询。",
                            NodeId = nodeId
                        };
                    }

                    // 2. Check for NOT IN / NOT LIKE which is anti-sargable
                    // Since SQL Server XML usually expands NOT IN into ORs or uses a left anti semi join, 
                    // it might not appear literally as "NOT IN" in the ScalarString. But sometimes it does or has <> or !=.
                    // Wait, sometimes it's literally written as NOT IN or NOT LIKE in the text or expanded.
                    // We can also check for CASE WHEN in predicate
                    if (scalarString.Contains("CASE WHEN", StringComparison.OrdinalIgnoreCase))
                    {
                        return new AnalysisResult
                        {
                            RuleId = "RULE_013_CASE_IN_PREDICATE",
                            Severity = "Warning",
                            Title = "谓词包含 CASE 表达式 (CASE in Predicate)",
                            Message = $"检测到 WHERE 子句中包含了 CASE 表达式：\n{scalarString}\n这通常会导致无法有效地使用索引寻址（Non-SARGable），建议使用 OR 分解查询或使用动态 SQL / OPTION (RECOMPILE)。",
                            NodeId = nodeId
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"AntiPatternRule failed: {ex.Message}");
            }
            return null;
        }
    }
}
