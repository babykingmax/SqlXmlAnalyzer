using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class CardinalityErrorRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_030_CARDINALITY_ERROR";
        public string Name => "Cardinality Estimation Deviation Detection";
        public string Description => "Detects significant mismatch between estimated and actual row counts with root causes.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                string physOp = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown";
                double estRows = PlanDiagnosticAnalyzer.ParseDouble(relOp.Attribute("EstimateRows")?.Value);

                double actRows = 0;
                double actExecs = 0;
                bool hasActual = false;

                var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                if (runTimeInfo != null)
                {
                    hasActual = true;
                    var counters = runTimeInfo.Descendants(ns + "RunTimeCountersPerThread");
                    foreach (var rc in counters)
                    {
                        if (rc == null) continue;
                        double r = PlanDiagnosticAnalyzer.ParseDouble(rc.Attribute("ActualRows")?.Value);
                        double e = PlanDiagnosticAnalyzer.ParseDouble(rc.Attribute("ActualExecutions")?.Value);
                        if (e < 1) e = 1;

                        actRows += r;
                        actExecs += e;
                    }
                }
                if (actExecs < 1) actExecs = 1;

                if (hasActual)
                {
                    double avgActRows = actRows / actExecs;
                    if (avgActRows > 100 || estRows > 100)
                    {
                        double diff = Math.Abs(estRows - avgActRows);
                        double ratio = Math.Max(estRows, avgActRows) / Math.Max(Math.Min(estRows, avgActRows), 1.0);
                        if (ratio > 10 && diff > 1000)
                        {
                            string reason = "";
                            string pred = PlanDiagnosticAnalyzer.ExtractPredicates(relOp, ns);
                            if (pred.Contains("AND"))
                            {
                                reason = " 🎯 根因: 存在多列联合过滤(AND)，多统计信息缺失或优化器低估。建议针对多列创建联合统计信息。";
                            }
                            else if (PlanDiagnosticAnalyzer.HasFunctionWrapper(pred))
                            {
                                reason = " 🎯 根因: 过滤列被标量函数包裹，导致统计信息失效。建议剥离标量函数。";
                            }

                            return new AnalysisResult
                            {
                                RuleId = this.RuleId,
                                Severity = "Critical",
                                Title = "基数估计误差",
                                Message = $"🚨 基数估计偏离 Node {nodeId} ({physOp}): 预估单次行数 {estRows:F0}，实际单次 {avgActRows:F0} (偏差达 {ratio:F1} 倍)。{reason}",
                                NodeId = nodeId
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"CardinalityErrorRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
