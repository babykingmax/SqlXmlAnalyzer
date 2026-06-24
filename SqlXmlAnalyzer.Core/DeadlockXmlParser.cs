using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    public static class DeadlockXmlParser
    {
        public static DeadlockParseResult<ParsedDeadlockGraphData> TryParseDeadlockXml(XDocument? doc)
        {
            if (doc?.Root == null)
            {
                return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("死锁 XML 缺少根节点。");
            }

            try
            {
                XElement? deadlockNode = FindDeadlockNode(doc);
                if (deadlockNode == null)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("未找到 deadlock 节点。");
                }

                XElement? processList = deadlockNode.Element("process-list");
                if (processList == null)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("死锁 XML 缺少必需的 process-list 节点。");
                }

                XElement? resourceList = deadlockNode.Element("resource-list");
                if (resourceList == null)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("死锁 XML 缺少必需的 resource-list 节点。");
                }

                var warnings = new List<string>();
                var processes = ParseProcesses(processList, warnings);
                if (processes.Count == 0)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("process-list 中没有包含有效 id 的 process。");
                }

                var resources = ParseResources(resourceList, warnings);
                if (resources.Count == 0)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("resource-list 中没有有效的锁资源。");
                }

                string victimId = deadlockNode
                    .Element("victim-list")?
                    .Element("victimProcess")?
                    .Attribute("id")?
                    .Value ?? string.Empty;

                if (victimId.Length == 0)
                {
                    warnings.Add("死锁 XML 未提供 victimProcess id。");
                }
                else if (processes.All(process => process.Id != victimId))
                {
                    warnings.Add($"victimProcess id '{victimId}' 未出现在 process-list 中。");
                }

                var processIds = processes
                    .Select(process => process.Id)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (LockResource resource in resources)
                {
                    foreach (string processId in resource.Owners.Select(owner => owner.Id)
                                 .Concat(resource.Waiters.Select(waiter => waiter.Id))
                                 .Where(id => id.Length > 0 && !processIds.Contains(id))
                                 .Distinct(StringComparer.Ordinal))
                    {
                        warnings.Add($"资源 {resource.Id} 引用了未知进程 '{processId}'。");
                    }
                }

                return DeadlockParseResult<ParsedDeadlockGraphData>.Success(
                    new ParsedDeadlockGraphData(processes, resources, victimId),
                    warnings);
            }
            catch (Exception ex)
            {
                Logger.LogException("DeadlockXmlParser.TryParseDeadlockXml", ex);
                return DeadlockParseResult<ParsedDeadlockGraphData>.Failure(
                    $"死锁 XML 解析失败: {ex.Message}");
            }
        }

        [Obsolete("Use TryParseDeadlockXml and inspect IsSuccess, Errors, and Warnings.")]
        public static (List<DeadlockProcess> processes, List<LockResource> resources, string victimId)
            ParseDeadlockXml(XDocument doc)
        {
            var result = TryParseDeadlockXml(doc);
            if (!result.IsSuccess || result.Value == null)
            {
                return (new List<DeadlockProcess>(), new List<LockResource>(), string.Empty);
            }

            return (
                result.Value.Processes.ToList(),
                result.Value.Resources.ToList(),
                result.Value.VictimId);
        }

        private static XElement? FindDeadlockNode(XDocument doc)
        {
            return doc.Root?.Name.LocalName == "deadlock"
                ? doc.Root
                : doc.Descendants().FirstOrDefault(element => element.Name.LocalName == "deadlock");
        }

        private static List<DeadlockProcess> ParseProcesses(
            XElement processList,
            List<string> warnings)
        {
            var processes = new List<DeadlockProcess>();

            foreach (XElement process in processList.Elements("process"))
            {
                string id = process.Attribute("id")?.Value ?? string.Empty;
                if (id.Length == 0)
                {
                    warnings.Add("已忽略缺少 id 的 process 节点。");
                    continue;
                }

                string spid = process.Attribute("spid")?.Value ?? string.Empty;
                if (spid.Length == 0)
                {
                    warnings.Add($"进程 {id} 缺少 spid。");
                }

                var frames = process.Element("executionStack")?.Elements("frame")
                    .Select(frame =>
                    {
                        string statement = (frame.Value ?? string.Empty).Trim();
                        if (statement.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                        {
                            statement = "[隐藏代码]";
                        }

                        return new ExecutionFrame(
                            frame.Attribute("procname")?.Value ?? string.Empty,
                            frame.Attribute("line")?.Value ?? string.Empty,
                            statement);
                    })
                    .ToList() ?? new List<ExecutionFrame>();

                processes.Add(new DeadlockProcess(
                    id,
                    spid,
                    process.Attribute("loginname")?.Value ?? string.Empty,
                    process.Attribute("hostname")?.Value ?? string.Empty,
                    process.Attribute("isolationlevel")?.Value ?? string.Empty,
                    process.Attribute("status")?.Value ?? string.Empty,
                    (process.Element("inputbuf")?.Value ?? string.Empty).Trim(),
                    frames,
                    process.Attribute("transactionname")?.Value ?? string.Empty,
                    process.Attribute("currentdbname")?.Value ?? string.Empty,
                    process.Attribute("clientapp")?.Value ?? string.Empty,
                    process.Attribute("waitresource")?.Value ?? string.Empty,
                    process.Attribute("waittime")?.Value ?? string.Empty,
                    process.Attribute("ecid")?.Value ?? string.Empty,
                    process.Attribute("currentdeadlockpriority")?.Value
                        ?? process.Attribute("deadlockpriority")?.Value
                        ?? "0",
                    process.Attribute("logused")?.Value ?? "0"));
            }

            return processes;
        }

        private static List<LockResource> ParseResources(
            XElement resourceList,
            List<string> warnings)
        {
            var resources = new List<LockResource>();
            int resourceIndex = 0;

            foreach (XElement resource in resourceList.Elements())
            {
                string resourceId = $"res_{resourceIndex++}";
                var owners = resource.Element("owner-list")?.Elements("owner")
                    .Select(owner => new LockOwner(
                        owner.Attribute("id")?.Value ?? string.Empty,
                        owner.Attribute("mode")?.Value ?? string.Empty))
                    .ToList() ?? new List<LockOwner>();
                var waiters = resource.Element("waiter-list")?.Elements("waiter")
                    .Select(waiter => new LockWaiter(
                        waiter.Attribute("id")?.Value ?? string.Empty,
                        waiter.Attribute("mode")?.Value ?? string.Empty,
                        waiter.Attribute("requestType")?.Value ?? string.Empty))
                    .ToList() ?? new List<LockWaiter>();

                if (owners.Count == 0)
                {
                    warnings.Add($"资源 {resourceId} 缺少 owner。");
                }
                if (waiters.Count == 0)
                {
                    warnings.Add($"资源 {resourceId} 缺少 waiter。");
                }

                resources.Add(new LockResource(
                    resource.Name.LocalName,
                    resource.Attribute("objectname")?.Value ?? "(未知)",
                    resource.Attribute("indexname")?.Value ?? string.Empty,
                    resource.Attribute("hobtid")?.Value ?? string.Empty,
                    resource.Attribute("dbid")?.Value ?? string.Empty,
                    owners,
                    waiters,
                    resourceId));
            }

            return resources;
        }
    }
}
