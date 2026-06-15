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
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                string physOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

                var threadRows = new Dictionary<string, double>();
                var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                if (runTimeInfo != null)
                {
                    var counters = runTimeInfo.Descendants(ns + "RunTimeCountersPerThread");
                    foreach (var rc in counters)
                    {
                        if (rc == null) continue;
                        string tid = rc.Attribute("Thread")?.Value ?? "0";
                        double r = PlanDiagnosticAnalyzer.ParseDouble(rc.Attribute("ActualRows")?.Value);
                        threadRows[tid] = r;
                    }
                }

                if (threadRows.Count > 1)
                {
                    var workerRows = threadRows.Where(kv => kv.Key != "0").Select(kv => kv.Value).ToList();
                    if (workerRows.Count > 1 && workerRows.Sum() > 1000)
                    {
                        double maxR = workerRows.Max();
                        double avgR = workerRows.Average();
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
            }
            catch (Exception ex)
            {
                Logger.Warning($"ThreadSkewRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
