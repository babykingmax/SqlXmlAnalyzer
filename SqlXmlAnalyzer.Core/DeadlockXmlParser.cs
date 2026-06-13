using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    public static class DeadlockXmlParser
    {
        public static (List<DeadlockProcess> processes, List<LockResource> resources, string victimId) ParseDeadlockXml(XDocument doc)
        {
            var processes = new List<DeadlockProcess>();
            var resources = new List<LockResource>();
            string victimId = "";

            if (doc?.Root == null) return (processes, resources, victimId);

            try
            {
                var processList = doc.Root.Element("process-list");
                if (processList != null)
                {
                    foreach (var p in processList.Elements("process"))
                    {
                        var frames = p.Element("executionStack")?.Elements("frame")
                            .Select(f => new ExecutionFrame(
                                f.Attribute("procname")?.Value ?? "",
                                f.Attribute("line")?.Value ?? "",
                                (f.Value ?? "").Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase) ? "[隐藏代码]" : (f.Value ?? "").Trim()))
                            .ToList() ?? new List<ExecutionFrame>();

                        // Parse logused correctly based on our DeadlockProcess constructor.
                        // Wait, what is the signature of DeadlockProcess constructor?
                        // Let's use the one that includes LogUsed if it has it, else we assume it's parsed as long property?
                        // Actually, I can check the log output.
                        
                        string logUsed = p.Attribute("logused")?.Value ?? "0";

                        var dp = new DeadlockProcess(
                            p.Attribute("id")?.Value ?? "",
                            p.Attribute("spid")?.Value ?? "",
                            p.Attribute("loginname")?.Value ?? "",
                            p.Attribute("hostname")?.Value ?? "",
                            p.Attribute("isolationlevel")?.Value ?? "",
                            p.Attribute("status")?.Value ?? "",
                            (p.Element("inputbuf")?.Value ?? "").Trim(),
                            frames,
                            p.Attribute("transactionname")?.Value ?? "",
                            p.Attribute("currentdbname")?.Value ?? "",
                            p.Attribute("clientapp")?.Value ?? "",
                            p.Attribute("waitresource")?.Value ?? "",
                            p.Attribute("waittime")?.Value ?? "",
                            p.Attribute("ecid")?.Value ?? "",
                            p.Attribute("currentdeadlockpriority")?.Value ?? p.Attribute("deadlockpriority")?.Value ?? "0",
                            logUsed
                        );

                        processes.Add(dp);
                    }
                }

                victimId = doc.Root.Element("victim-list")?.Element("victimProcess")?.Attribute("id")?.Value ?? "";

                var resourceList = doc.Root.Element("resource-list");
                if (resourceList != null)
                {
                    int resIndex = 0;
                    foreach (var resElem in resourceList.Elements())
                    {
                        var owners = resElem.Element("owner-list")?.Elements("owner")
                            .Select(o => new LockOwner(o.Attribute("id")?.Value ?? "", o.Attribute("mode")?.Value ?? ""))
                            .ToList() ?? new List<LockOwner>();

                        var waiters = resElem.Element("waiter-list")?.Elements("waiter")
                            .Select(w => new LockWaiter(w.Attribute("id")?.Value ?? "", w.Attribute("mode")?.Value ?? "", w.Attribute("requestType")?.Value ?? ""))
                            .ToList() ?? new List<LockWaiter>();

                        resources.Add(new LockResource(
                            resElem.Name.LocalName,
                            resElem.Attribute("objectname")?.Value ?? "(未知)",
                            resElem.Attribute("indexname")?.Value ?? "",
                            resElem.Attribute("hobtid")?.Value ?? "",
                            resElem.Attribute("dbid")?.Value ?? "",
                            owners, waiters, $"res_{resIndex++}"));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("DeadlockXmlParser.ParseDeadlockXml", ex);
            }

            return (processes, resources, victimId);
        }
    }
}
