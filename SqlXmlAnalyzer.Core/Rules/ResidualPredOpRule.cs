using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ResidualPredOpRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_034_RESIDUAL_PRED_OP";
        public string Name => "Residual Predicate Operator Detection";
        public string Description => "Detects residual predicates on Index Seek operators that cause excess IO overhead.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var nodeId = relOp.Attribute("NodeId")?.Value ?? "0";
                var physOp = relOp.Attribute("PhysicalOp")?.Value ?? "";

                if (physOp.Contains("Seek") && !string.IsNullOrEmpty(PlanDiagnosticAnalyzer.ExtractSeekPredicate(relOp, ns)))
                {
                    var residualPred = PlanDiagnosticAnalyzer.ExtractResidualPredicate(relOp, ns);
                    if (!string.IsNullOrEmpty(residualPred))
                    {
                        double actRows = 0;
                        var runTimeInfo = relOp.Element(ns + "RunTimeInformation");
                        if (runTimeInfo != null)
                        {
                            var counters = runTimeInfo.Descendants(ns + "RunTimeCountersPerThread");
                            foreach (var rc in counters)
                            {
                                if (rc == null) continue;
                                actRows += PlanDiagnosticAnalyzer.ParseDouble(rc.Attribute("ActualRows")?.Value);
                            }
                        }

                        bool isResidualWarning = false;
                        double actRowsRead = 0;
                        var runtime = relOp.Element(ns + "RunTimeInformation");
                        if (runtime != null)
                        {
                            var counters = runtime.Descendants(ns + "RunTimeCountersPerThread");
                            foreach (var rc in counters)
                            {
                                if (rc == null) continue;
                                actRowsRead += PlanDiagnosticAnalyzer.ParseDouble(rc.Attribute("ActualRowsRead")?.Value);
                            }
                        }

                        if (actRows > 0 && actRowsRead > actRows * 1.2 && (actRowsRead - actRows) > 100)
                        {
                            isResidualWarning = true;
                        }
                        else if (runtime == null)
                        {
                            isResidualWarning = true;
                        }

                        if (isResidualWarning)
                        {
                            string objName = PlanDiagnosticAnalyzer.ExtractObjectName(relOp, ns);
                            return new AnalysisResult
                            {
                                RuleId = this.RuleId,
                                Severity = "Warning",
                                Title = "寻址残差谓词漏洞",
                                Message = $"🚨 残差谓词 Node {nodeId} ({physOp} on {objName}): 定位虽走 Seek 索引，但在过滤列上只有前导列被用于 Seek，其他过滤列作为了“残差谓词”在内存中强行二次比对过滤，带来多余 IO 开销。建议把 [{residualPred}] 包含的列加入复合索引。 [DBA 提示] 若当前为并行查询且启用了 Bitmap Filter，ActualRowsRead 可能偏高，需结合实际执行时间综合判断。",
                                NodeId = nodeId
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ResidualPredOpRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
