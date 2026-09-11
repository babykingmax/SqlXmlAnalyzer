using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class MemorySpillRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_032_MEMORY_SPILL";
        public string Name => "Memory Spill Detection";
        public string Description => "Detects memory spills to TempDB in sort or hash operations.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
            string physOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

            var warningsEl = relOp.Element(ns + "Warnings");
            if (warningsEl != null)
            {
                var warnList = warningsEl.Elements().Where(e => e.Name.Namespace == ns
                    && e.Name.LocalName is "SpillToTempDb" or "HashSpillDetails" or "SortSpillDetails" or "ExchangeSpillDetails")
                    .Select(e => e.Name.LocalName).Distinct().ToList();
                if (warnList.Count > 0)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Critical",
                        Title = "内存预估与溢出落盘",
                        Message = $"算子 Node {nodeId} ({physOp}) 包含 TempDB 溢出证据：[ {string.Join(", ", warnList)} ]。请结合溢出规模、实际行数与内存授予核对影响；单凭该警告不能确定内存估算偏差或根因。",
                        NodeId = nodeId
                    };
                }
            }

            return null;
        }
    }
}
