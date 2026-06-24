using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class QueryRewriteRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_025_QUERY_REWRITE";
        public string Name => "Query Rewrite Suggestion";
        public string Description => "Generates SQL rewrite suggestions based on query structure.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var messages = new List<string>();

                // Check for implicit conversion
                bool hasConv = doc.Descendants().Any(e => e.Attribute("ScalarString")?.Value.Contains("CONVERT_IMPLICIT") == true || e.Attribute("Expression")?.Value.Contains("CONVERT_IMPLICIT") == true);
                if (hasConv)
                {
                    messages.Add("💡 [隐式转换修复]\n-- ❌ 原写法 (导致全表扫描):\nSELECT * FROM Table WHERE VarcharCol = N'123' \n-- ✅ 优化 (恢复 Index Seek):\nSELECT * FROM Table WHERE VarcharCol = CAST('123' AS VARCHAR(100))");
                }

                // Check for UDF or Table variables
                bool hasUdf = doc.Descendants(ns + "RelOp").Any(r => r.Attribute("PhysicalOp")?.Value == "Table Valued Function" || r.Attribute("LogicalOp")?.Value == "Table Valued Function");
                if (hasUdf)
                {
                    messages.Add("💡 [表变量性能黑洞修复]\n-- ❌ 原写法 (缺乏统计信息):\nDECLARE @Tmp TABLE (Id INT);\nINSERT INTO @Tmp...\n\n-- ✅ 优化 (临时表有独立直方图支持分布):\nCREATE TABLE #Tmp (Id INT);\nINSERT INTO #Tmp...\n-- (记得 DROP TABLE #Tmp)");
                }

                // Parameter sensitivity requires direct evidence that the compiled and
                // runtime parameter values differ. Statistics usage alone is normal
                // optimizer behavior and must not trigger parameter-sniffing advice.
                bool hasSniff = doc.Descendants(ns + "ColumnReference").Any(p => p.Attribute("ParameterCompiledValue")?.Value != null && p.Attribute("ParameterRuntimeValue")?.Value != null && p.Attribute("ParameterCompiledValue")?.Value != p.Attribute("ParameterRuntimeValue")?.Value);
                if (hasSniff)
                {
                    messages.Add("💡 [参数嗅探 4 种解法]\n👉 解法 A (表小/查询快): 在末尾加 `OPTION (RECOMPILE)`\n👉 解法 B (查询极复杂): 在末尾加 `OPTION (OPTIMIZE FOR UNKNOWN)`\n👉 解法 C (查询极偏斜): 在末尾加 `OPTION (OPTIMIZE FOR (@p = '典型值'))`\n👉 解法 D (存储过程局部变量骗过优化器): \n   DECLARE @L_Var INT = @Param;\n   SELECT ... WHERE Col = @L_Var;");
                }

                if (messages.Any())
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "Query Rewrite Blocks",
                        Message = string.Join("|||", messages),
                        NodeId = "0"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"QueryRewriteRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
