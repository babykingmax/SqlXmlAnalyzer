using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class MemoryGrantDocRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_029_MEMORY_GRANT_DOC";
        public string Name => "Memory Grant Overestimation Detection";
        public string Description => "Detects memory grant overestimation with low memory utilization.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            if (relOp.Attribute("NodeId")?.Value != "0") return null;

            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var memGrant = doc.Descendants(ns + "MemoryGrantInfo").FirstOrDefault();
                if (memGrant != null)
                {
                    double granted = PlanDiagnosticAnalyzer.ParseDouble(memGrant.Attribute("GrantedMemory")?.Value);
                    double used = PlanDiagnosticAnalyzer.ParseDouble(memGrant.Attribute("MaxUsedMemory")?.Value);
                    if (granted > 10240 && used > 0 && (used / granted) < 0.1)
                    {
                        return new AnalysisResult
                        {
                            RuleId = this.RuleId,
                            Severity = "Warning",
                            Title = "内存预估与溢出落盘",
                            Message = $"💾 [资源空置浪费]: 内存预估过度！本查询总共申请排队并占用了 {granted / 1024.0:F1} MB 内存，但实际运行中仅最大消耗了 {used / 1024.0:F1} MB (内存利用率低于 10%)。并发极易导致 RESOURCE_SEMAPHORE 锁等待，建议更新涉及表的统计信息。",
                            NodeId = "0"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"MemoryGrantDocRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
