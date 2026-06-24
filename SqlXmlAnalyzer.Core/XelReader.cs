using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.SqlServer.XEvent.XELite;

namespace SqlXmlAnalyzer.Core
{
    public class XelDeadlockReport
    {
        public string Timestamp { get; set; } = string.Empty;
        public string DeadlockXml { get; set; } = string.Empty;
    }

    public class XelReader
    {
        public async Task<List<XelDeadlockReport>> ReadDeadlocksAsync(
            string xelFilePath,
            CancellationToken cancellationToken = default)
        {
            var reports = new List<XelDeadlockReport>();
            var reader = new XEFileEventStreamer(xelFilePath);

            await reader.ReadEventStream(
                xevent =>
                {
                    if (xevent.Name == "xml_deadlock_report")
                    {
                        if (xevent.Fields.TryGetValue("xml_report", out var xmlReportObj))
                        {
                            reports.Add(new XelDeadlockReport
                            {
                                Timestamp = xevent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                                DeadlockXml = xmlReportObj?.ToString() ?? ""
                            });
                        }
                    }
                    return Task.CompletedTask;
                },
                cancellationToken);

            return reports;
        }
    }
}
