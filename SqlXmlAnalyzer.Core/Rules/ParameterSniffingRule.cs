using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ParameterSniffingRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_003_PARAM_SNIFFING";
        public string Name => "Parameter Sniffing Detection";
        public string Description => "Detects parameter sniffing by comparing compiled and runtime parameter values.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                
                // Only execute this rule on the Root node (NodeId = 0) to avoid duplicate warnings
                if (nodeId != "0" && nodeId != "1") return null;

                var paramList = relOp.Document?.Descendants(ns + "ParameterList").Descendants(ns + "ColumnReference");
                if (paramList == null) return null;

                var sniffedParams = new List<string>();

                foreach (var p in paramList)
                {
                    string col = p.Attribute("Column")?.Value ?? "";
                    string? comp = p.Attribute("ParameterCompiledValue")?.Value;
                    string? run = p.Attribute("ParameterRuntimeValue")?.Value;

                    if (!string.IsNullOrEmpty(comp) && !string.IsNullOrEmpty(run) && comp != run)
                    {
                        sniffedParams.Add($"{col} (编译值: {comp}, 运行值: {run})");
                    }
                }

                if (sniffedParams.Any())
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "参数嗅探风险 (Parameter Sniffing)",
                        Message = "检测到编译期参数与运行时参数值不一致，可能导致次优的执行计划：\n" + 
                                  string.Join("\n", sniffedParams) + 
                                  "\n建议：如果遇到性能突降，可考虑使用 OPTION (RECOMPILE) 或 OPTIMIZE FOR 提示。",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ParameterSniffingRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
