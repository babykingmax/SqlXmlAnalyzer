using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class MultipleScalarSubqueriesRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_024_SCALAR_SUBQUERY_PATTERN";
        public string Name => "Multiple Scalar Subqueries Detection";
        public string Description => "Detects multiple scalar subqueries in the SELECT clause.";

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
                foreach (var stmt in stmts)
                {
                    if (stmt == null) continue;
                    string sqlText = stmt.Attribute("StatementText")?.Value ?? "";
                    if (!string.IsNullOrEmpty(sqlText))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(sqlText, @"\bFROM\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string selectClause = sqlText.Substring(0, match.Index);
                            int scalarCount = System.Text.RegularExpressions.Regex.Matches(selectClause, @"\(\s*SELECT\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
                            if (scalarCount >= 2)
                            {
                                return new AnalysisResult
                                {
                                    RuleId = this.RuleId,
                                    Severity = "Warning",
                                    Title = "标量子查询反模式",
                                    Message = $"🚨 **[设计缺陷] SELECT 列表中检测到 {scalarCount} 个标量子查询！**\n   每个子查询等同于每一行触发一次单独的隐式游标调用，造成性能灾难。\n   👉 **重构建议: 强制整合为一个 (LEFT JOIN) 或使用 CROSS APPLY 统一计算。**",
                                    NodeId = "0"
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"MultipleScalarSubqueriesRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
