using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Rules
{
    public class WaitStatsRule : IPlanAnalyzerRule
    {
        public string RuleId => "RULE_016_WAIT_STATS";
        public string Name => "Wait Stats Detection";
        public string Description => "Detects significant resource wait stats.";

        public AnalysisResult? Analyze(XElement relOp, XNamespace ns)
        {
            if (relOp.Attribute("NodeId")?.Value != "0") return null;

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

                    if (!wtype.Contains("RESOURCE_SEMAPHORE") && wtime > 100)
                    {
                        messages.Add($"⏱️ 发现显著资源等待 [{wtype}]: 累积耗时高达 {wtime:F0} 毫秒。");
                    }
                }

                if (messages.Any())
                {
                    return new AnalysisResult
                    {
                        RuleId = this.RuleId,
                        Severity = "Warning",
                        Title = "发现显著资源等待",
                        Message = string.Join("|||", messages),
                        NodeId = "0"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"WaitStatsRule failed: {ex.Message}");
            }

            return null;
        }
    }
}
