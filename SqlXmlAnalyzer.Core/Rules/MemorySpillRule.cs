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
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                string physOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

                var warningsEl = relOp.Element(ns + "Warnings");
                if (warningsEl != null)
                {
                    var warnList = warningsEl.Elements().Where(e => e != null).Select(e => e.Name.LocalName).ToList();
                    if (warnList.Count > 0)
                    {
                        return new AnalysisResult
                        {
                            RuleId = this.RuleId,
                            Severity = "Critical",
                            Title = "内存预估与溢出落盘",
                            Message = $"⚠️ 算子告警 Node {nodeId} ({physOp}): 执行引擎爆发了 [ {string.Join(", ", warnList)} ] 警告！发生了排序或哈希的溢出并被迫落盘 TempDB！IO 性能遭受了毁灭性打击！",
                            NodeId = nodeId
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"MemorySpillRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
