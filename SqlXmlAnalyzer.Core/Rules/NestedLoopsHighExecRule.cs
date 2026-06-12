using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class NestedLoopsHighExecRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_012_NESTED_LOOPS_HIGH_EXEC";
        public string Name => "Nested Loops High Executions";
        public string Description => "Detects Nested Loops operators with an extremely high number of executions, indicating a potential missing index or bad join choice.";

        private const double HIGH_EXEC_THRESHOLD = 10000.0;

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "N/A";
                var physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

                // Only evaluate on operators that perform loops or lookups
                if (!physicalOp.Contains("Nested Loops") && physicalOp != "Key Lookup" && physicalOp != "Clustered Index Seek")
                {
                    return null;
                }

                var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                if (runTimeInfo == null) return null;

                double maxExecutions = 0;
                double totalActualRows = 0;

                foreach (var counter in runTimeInfo.Elements(ns + "RunTimeCountersPerThread"))
                {
                    if (double.TryParse(counter.Attribute("ActualExecutions")?.Value, out double execs))
                    {
                        maxExecutions = Math.Max(maxExecutions, execs);
                    }
                    if (double.TryParse(counter.Attribute("ActualRows")?.Value, out double actualRows))
                    {
                        totalActualRows += actualRows;
                    }
                }

                if (maxExecutions >= HIGH_EXEC_THRESHOLD)
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Critical",
                        Title = $"嵌套循环执行次数过高 ({physicalOp})",
                        Message = $"此操作符被循环执行了 {maxExecutions:N0} 次，总计返回 {totalActualRows:N0} 行！\n极高的执行次数会消耗大量 CPU，通常是因为驱动表返回了过多数据，或缺少合适的索引导致优化器错误地选择了嵌套循环。\n建议：检查连接条件，或考虑使用 Hash Join / Merge Join 提示，并确保内部表拥有良好索引。",
                        NodeId = nodeId
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"NestedLoopsHighExecRule failed: {ex.Message}");
            }
            return null;
        }
    }
}
