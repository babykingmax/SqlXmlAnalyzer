using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ParallelSkewRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_009_PARALLEL_SKEW";
        public string Name => "Thread Skew / Ineffective Parallelism";
        public string Description => "Detects unbalanced parallel execution (thread skew) or ineffective parallelism where thread overhead outweighs benefits.";

        private double GetSkewThreshold(int dop) => dop <= 2 ? 0.6 : 0.5;

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                var physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown";

                bool isParallel = relOp.Attribute("Parallel")?.Value == "1" || relOp.Attribute("Parallel")?.Value?.ToLower() == "true";
                if (!isParallel) return null;

                var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                if (runTimeInfo == null) return null;

                var threads = runTimeInfo.Elements(ns + "RunTimeCountersPerThread").ToList();
                if (!threads.Any()) return null;

                // Extract threads
                var threadStats = threads.Select(t => new
                {
                    ThreadId = int.TryParse(t.Attribute("Thread")?.Value, out int tid) ? tid : 0,
                    ActualRows = double.TryParse(t.Attribute("ActualRows")?.Value, out double rows) ? rows : 0
                }).ToList();

                double totalRows = threadStats.Sum(t => t.ActualRows);

                // Exclude thread 0 (coordinator) for data skew analysis
                var workerThreads = threadStats.Where(t => t.ThreadId != 0).ToList();
                if (!workerThreads.Any()) return null;

                int actualDop = workerThreads.Count;
                double maxWorkerRows = workerThreads.Max(t => t.ActualRows);

                // Check Ineffective Parallelism
                if (actualDop > 4 && (totalRows / actualDop) < 100 && totalRows > 0)
                {
                    return new AnalysisResult
                    {
                        RuleId = "RULE_010_INEFFECTIVE_PARALLELISM",
                        Severity = "Info",
                        Title = $"低效的并行执行 ({physicalOp})",
                        Message = $"使用了较高的并行度 (DOP={actualDop})，但每个线程分配到的行数极少 (平均: {(totalRows / actualDop):F0} 行)。\n线程调度开销可能大于并行收益，建议适当调低 MAXDOP (最大并行度)。",
                        NodeId = nodeId
                    };
                }

                // Check Thread Skew
                if (totalRows > 1000)
                {
                    double skewRatio = maxWorkerRows / totalRows;
                    double threshold = GetSkewThreshold(actualDop);

                    if (skewRatio > threshold)
                    {
                        return new AnalysisResult
                        {
                            RuleId = this.RuleId,
                            Severity = "Warning",
                            Title = $"并行线程分布倾斜 ({physicalOp})",
                            Message = $"并行度 DOP={actualDop}。最忙碌的单线程承担了 {(skewRatio * 100):F0}% 的工作量 ({maxWorkerRows:N0} / {totalRows:N0} 行)。\n强烈建议：检查 JOIN 或 GROUP BY 聚合键上是否存在严重的数据分布倾斜。",
                            NodeId = nodeId
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ParallelSkewRule failed: {ex.Message}");
            }
            return null;
        }
    }
}
