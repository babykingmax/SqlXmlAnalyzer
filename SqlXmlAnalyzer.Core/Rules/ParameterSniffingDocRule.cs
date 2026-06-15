using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ParameterSniffingDocRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_027_PARAM_SNIFFING_DOC";
        public string Name => "Parameter Sniffing Document Rule";
        public string Description => "Detects compile vs runtime parameter value differences across the document.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            if (relOp.Attribute("NodeId")?.Value != "0") return null;

            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var paramCols = doc.Descendants(ns + "ParameterList").Descendants(ns + "ColumnReference");
                var messages = new List<string>();

                foreach (var p in paramCols)
                {
                    if (p == null) continue;
                    string col = p.Attribute("Column")?.Value ?? "";
                    string? comp = p.Attribute("ParameterCompiledValue")?.Value;
                    string? run = p.Attribute("ParameterRuntimeValue")?.Value;
                    if (!string.IsNullOrEmpty(comp) && !string.IsNullOrEmpty(run) && comp != run)
                    {
                        messages.Add($"🧵 参数嗅探警告 on {col}:\n   • 首次编译缓存值 (Compiled): [{comp}]\n   • 运行时传入值 (Runtime): [{run}]\n   👉 [专家处方]: 首次编译值和实际运行时参数不同，当两值数据分布差异极大时极易引发“嗅探灾难”（选用次优查询方案导致运行缓慢）。建议对该 SQL 语句末尾附加 `OPTION (RECOMPILE)` 提示。");
                    }
                }

                if (messages.Any())
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "参数嗅探反模式",
                        Message = string.Join("|||", messages),
                        NodeId = "0"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ParameterSniffingDocRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
