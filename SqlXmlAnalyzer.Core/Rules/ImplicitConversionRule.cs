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
                        Title = "Implicit Conversion Risk",
                        Message = msg,
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
