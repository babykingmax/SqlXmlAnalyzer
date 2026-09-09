using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ThreadSkewRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_033_THREAD_SKEW";
        public string Name => "Parallel Thread Data Skew Detection";
        public string Description => "Detects parallel data distribution skew among execution threads.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
            string physOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

            var workers = Services.PlanOperatorFactsService.Get(relOp, ns).WorkerRows;
            if (workers is { Count: > 1 })
            {
                if (workers.Total > 1000)
                {
                    double maxR = (double)workers.Maximum;
                    double avgR = (double)workers.Average;
                    if (maxR > avgR * 2.0 && maxR > 100)
                    {
                        return new AnalysisResult
                        {
                            RuleId = this.RuleId,
                            Severity = "Warning",
                            Title = "并行数据倾斜瓶颈",
                            Message = $"⚡ 线程倾斜 Node {nodeId} ({physOp}): 并行数据倾斜！最大线程分配了 {maxR:F0} 行 (平均行数仅 {avgR:F0})。拖慢了整体吞吐速度。",
                            NodeId = nodeId
                        };
                    }
                }
            }

            return null;
        }
    }
}
