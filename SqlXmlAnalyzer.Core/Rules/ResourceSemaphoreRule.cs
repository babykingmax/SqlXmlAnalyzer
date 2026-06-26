using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class ResourceSemaphoreRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_037_RESOURCE_SEMAPHORE";
        public string Name => "Resource Semaphore Wait Detection";
        public string Description => "Detects memory grant resource semaphore waits.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            try
            {
                var doc = relOp.Document;
                if (doc == null) return null;

                var waitStats = doc.Descendants(ns + "WaitStats").Descendants(ns + "Wait");
                var messages = new List<string>();

                foreach (var ws in waitStats)
                {
                    if (ws == null) continue;
                    string wtype = ws.Attribute("WaitType")?.Value ?? "";
                    double wtime = PlanDiagnosticAnalyzer.ParseDouble(ws.Attribute("WaitTimeMs")?.Value);

                    if (wtype.Contains("RESOURCE_SEMAPHORE"))
                    {
                        messages.Add($"🚦 内存准入排队 [{wtype}]: 耗时 {wtime:F0} 毫秒。这表示服务器并发查询消耗了大量内存，当前查询被迫排队等待内存分配 (Memory Grant Waiting)。严重影响整体吞吐率！");
                    }
                }

                if (messages.Any())
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Critical",
                        Title = "内存资源准入等待",
                        Message = string.Join("|||", messages),
                        NodeId = "0"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ResourceSemaphoreRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
