using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class LocalVariablesRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_015_LOCAL_VARIABLES";
        public string Name => "Local Variables in Predicates";
        public string Description => "Detects use of local variables in predicates which can lead to poor cardinality estimates.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                var stmtSimple = relOp.Ancestors(ns + "StmtSimple").FirstOrDefault()
                    ?? relOp.Document?.Descendants(ns + "StmtSimple").FirstOrDefault();
                if (stmtSimple == null) return null;

                string statementText = stmtSimple.Attribute("StatementText")?.Value ?? "";
                if (string.IsNullOrEmpty(statementText)) return null;

                // Regex match DECLARE @var ... and later WHERE @var = ...
                var localVarPattern = new System.Text.RegularExpressions.Regex(@"DECLARE\s+@(\w+)\s+[\w\(\)]+;.*?WHERE.*?@\1\s*[=<>]", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                var matches = localVarPattern.Matches(statementText);

                if (matches.Count > 0)
                {
                    var vars = matches.Cast<System.Text.RegularExpressions.Match>().Select(m => "@" + m.Groups[1].Value).Distinct();
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning", // Can be overridden to Medium or Warning
                        Title = "本地变量导致基数误判 (Local Variables)",
                        Message = $"检测到本地变量 [{string.Join(", ", vars)}] 被用于 WHERE 条件，可能导致基数误估（预估 1 行）。\n建议：\n1. 将本地变量替换为存储过程参数，让优化器了解真实值。\n2. 添加 OPTION (RECOMPILE) 强制每次重新编译。\n3. 使用 OPTION (OPTIMIZE FOR (@var = '典型值')) 提供代表性值。",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("LocalVariablesRule failed", ex);
            }
            return null;
        }
    }
}
