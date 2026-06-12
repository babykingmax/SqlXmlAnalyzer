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
                // Execute only on root nodes
                if (nodeId != "0" && nodeId != "1") return null;

                var stmtSimple = relOp.Document?.Descendants(ns + "StmtSimple").FirstOrDefault();
                if (stmtSimple == null) return null;

                string statementText = stmtSimple.Attribute("StatementText")?.Value ?? "";
                
                // Extremely naive heuristic: checking if StatementText has DECLARE and a WHERE clause using variables.
                // In a real parser, we'd check if the ScalarString of predicates contains local variables without parameter list.
                // Actually, the best way in Showplan XML is to check if there are no ParameterList entries but predicates contain variables like [@VariableName]
                
                var paramList = stmtSimple.Descendants(ns + "ParameterList").FirstOrDefault();
                var hasParams = paramList != null;

                // Look for variables in scalar strings that start with @ but are not in parameter list
                var allScalars = relOp.DescendantsAndSelf(ns + "ScalarOperator")
                                      .Select(s => s.Attribute("ScalarString")?.Value)
                                      .Where(v => !string.IsNullOrEmpty(v))
                                      .ToList();

                bool usesLocalVariable = false;
                string foundVar = "";

                foreach (var scalar in allScalars)
                {
                    // Basic heuristic: contains [@var] and statement has DECLARE
                    if (scalar.Contains("[@") && statementText.Contains("DECLARE", StringComparison.OrdinalIgnoreCase))
                    {
                        usesLocalVariable = true;
                        foundVar = scalar;
                        break;
                    }
                }

                if (usesLocalVariable && !hasParams) // Simplified trigger
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "本地变量导致基数误判 (Local Variables)",
                        Message = $"检测到在 WHERE/JOIN 条件中使用了本地变量: {foundVar}。\n由于本地变量的值在编译时不可知，优化器会使用平均密度进行硬编码的盲目预估。\n建议方案：\n1. 将本地变量直接替换为存储过程的传入参数。\n2. 若无法修改，使用 OPTION (RECOMPILE) 强制每次执行时嗅探本地变量的值。",
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
