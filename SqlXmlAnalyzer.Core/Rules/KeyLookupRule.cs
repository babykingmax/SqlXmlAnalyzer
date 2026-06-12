using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class KeyLookupRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_002_KEY_LOOKUP";
        public string Name => "Key/RID Lookup Detection";
        public string Description => "Detects high-cost Key or RID lookups that can be optimized using covering indexes.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                var physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

                if (physicalOp == "Key Lookup" || physicalOp == "RID Lookup")
                {
                    string objName = "Unknown";
                    var objNode = relOp.Descendants(ns + "Object").FirstOrDefault();
                    if (objNode != null)
                    {
                        string table = objNode.Attribute("Table")?.Value?.Trim('[', ']') ?? "";
                        string index = objNode.Attribute("Index")?.Value?.Trim('[', ']') ?? "";
                        objName = string.IsNullOrEmpty(index) ? table : $"{table}.{index}";
                    }

                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = physicalOp + " Detected",
                        Message = $"Detected {physicalOp} on {objName}. Consider using a Covering Index (INCLUDE columns) or review if SELECT * is fetching unnecessary columns.",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"KeyLookupRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
