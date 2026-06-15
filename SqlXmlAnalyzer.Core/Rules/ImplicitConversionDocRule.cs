using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ImplicitConversionDocRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_026_IMPLICIT_CONV_DOC";
        public string Name => "Implicit Conversion Document Rule";
        public string Description => "Detects CONVERT_IMPLICIT across the entire document.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            if (relOp.Attribute("NodeId")?.Value != "0") return null;

            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var convs = new HashSet<string>();
                var scalarOps = doc.Descendants(ns + "ScalarOperator");
                foreach (var op in scalarOps)
                {
                    if (op == null) continue;
                    string s = op.Attribute("ScalarString")?.Value ?? "";
                    if (s.Contains("CONVERT_IMPLICIT"))
                    {
                        convs.Add(s);
                    }
                }
                var pacs = doc.Descendants(ns + "PlanAffectingConvert");
                foreach (var pac in pacs)
                {
                    if (pac == null) continue;
                    string expr = pac.Attribute("Expression")?.Value ?? "";
                    if (expr.Contains("CONVERT_IMPLICIT"))
                    {
                        convs.Add(expr);
                    }
                }

                var messages = new List<string>();
                foreach (var c in convs.Distinct())
                {
                    messages.Add($"⚠️ 隐式转换风险: SQL 引擎执行了 CONVERT_IMPLICIT。这通常由于字段类型不匹配引起，极易导致索引扫描失效（Index Scan）：\n   👉 表达式: {c}");
                }

                if (messages.Any())
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "隐式转换风险",
                        Message = string.Join("|||", messages),
                        NodeId = "0"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ImplicitConversionDocRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
