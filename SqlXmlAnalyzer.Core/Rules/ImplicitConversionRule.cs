using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ImplicitConversionRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_001_IMPLICIT_CONV";
        public string Name => "Implicit Conversion Detection";
        public string Description => "Detects CONVERT_IMPLICIT operations which can prevent index seeks and degrade performance.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                
                // Find all CONVERT_IMPLICIT strings in ScalarOperator
                var implicitConvs = relOp.Descendants(ns + "ScalarOperator")
                    .Select(op => op.Attribute("ScalarString")?.Value)
                    .Where(s => !string.IsNullOrEmpty(s) && s.Contains("CONVERT_IMPLICIT"))
                    .ToList();

                if (implicitConvs.Any())
                {
                    // Evaluate Severity
                    // If CONVERT_IMPLICIT happens on an Index Scan or Table Scan, it might be the reason it didn't Seek.
                    string physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "";
                    string severity = "Warning";
                    
                    if (physicalOp.Contains("Scan"))
                    {
                        severity = "Critical";
                    }

                    string msg = $"Detected {implicitConvs.Count} implicit conversion(s).\nExample: {implicitConvs.First()}";

                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = severity,
                        Title = "隐式转换风险 (Implicit Conversion)",
                        Message = $"检测到 {implicitConvs.Count} 处隐式转换。\n隐式转换会导致索引失效，引发全表扫描。\n示例代码: {implicitConvs.First()}",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ImplicitConversionRule failed on node: {ex.Message}");
            }

            return null;
        }
    }
}
