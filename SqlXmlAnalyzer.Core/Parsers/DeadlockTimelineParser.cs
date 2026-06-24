using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Parsers
{
    public class DeadlockTimelineParser
    {
        public class ParsedDeadlock
        {
            public List<DeadlockEvent> Events { get; set; } = new();
            public Dictionary<string, DeadlockNodeInfo> Processes { get; set; } = new();
            public Dictionary<string, DeadlockResourceInfo> Resources { get; set; } = new();
        }

        public DeadlockParseResult<ParsedDeadlock> ParseResult(string xmlContent)
        {
            if (string.IsNullOrWhiteSpace(xmlContent))
            {
                return DeadlockParseResult<ParsedDeadlock>.Failure("死锁 XML 内容为空。");
            }

            try
            {
                XDocument doc = SafeXmlHelper.ParseSafe(xmlContent);
                XElement? deadlockNode = doc.Root?.Name.LocalName == "deadlock"
                    ? doc.Root
                    : doc.Descendants().FirstOrDefault(element => element.Name.LocalName == "deadlock");
                if (deadlockNode == null)
                {
                    return DeadlockParseResult<ParsedDeadlock>.Failure("未找到 deadlock 节点。");
                }

                var graphParseResult = DeadlockXmlParser.TryParseDeadlockXml(
                    new XDocument(new XElement(deadlockNode)));
                if (!graphParseResult.IsSuccess)
                {
                    return new DeadlockParseResult<ParsedDeadlock>(
                        null,
                        graphParseResult.Errors,
                        graphParseResult.Warnings);
                }

                var parsed = BuildTimeline(deadlockNode);
                return DeadlockParseResult<ParsedDeadlock>.Success(
                    parsed,
                    graphParseResult.Warnings);
            }
            catch (Exception ex)
            {
                Logger.LogException("DeadlockTimelineParser.ParseResult", ex);
                return DeadlockParseResult<ParsedDeadlock>.Failure(
                    $"死锁时间线解析失败: {ex.Message}");
            }
        }

        [Obsolete("Use ParseResult and inspect IsSuccess, Errors, and Warnings.")]
        public ParsedDeadlock Parse(string xmlContent)
        {
            return ParseResult(xmlContent).Value ?? new ParsedDeadlock();
        }

        private static ParsedDeadlock BuildTimeline(XElement deadlockNode)
        {
            var result = new ParsedDeadlock();
            string victimId = deadlockNode.Element("victim-list")
                ?.Element("victimProcess")
                ?.Attribute("id")
                ?.Value ?? string.Empty;

            foreach (XElement process in deadlockNode.Element("process-list")!.Elements("process"))
            {
                string id = process.Attribute("id")?.Value ?? string.Empty;
                if (id.Length == 0)
                {
                    continue;
                }

                result.Processes[id] = new DeadlockNodeInfo
                {
                    Id = id,
                    Spid = process.Attribute("spid")?.Value ?? string.Empty,
                    IsVictim = id == victimId
                };
            }

            var adjacency = new Dictionary<string, List<string>>();
            var resourceToWaiters = new Dictionary<string, List<string>>();
            var resourceToOwners = new Dictionary<string, List<string>>();
            var grants = new List<DeadlockEvent>();
            var requests = new List<DeadlockEvent>();
            int resourceIndex = 0;

            foreach (XElement resource in deadlockNode.Element("resource-list")!.Elements())
            {
                string resourceId = $"res_{resourceIndex++}";
                result.Resources[resourceId] = new DeadlockResourceInfo
                {
                    Id = resourceId,
                    Name = resource.Name.LocalName
                };
                resourceToWaiters[resourceId] = new List<string>();
                resourceToOwners[resourceId] = new List<string>();

                foreach (XElement owner in resource.Element("owner-list")?.Elements("owner")
                             ?? Enumerable.Empty<XElement>())
                {
                    string processId = owner.Attribute("id")?.Value ?? string.Empty;
                    string lockMode = owner.Attribute("mode")?.Value ?? string.Empty;
                    resourceToOwners[resourceId].Add(processId);
                    string spid = GetSpid(result, processId);
                    grants.Add(new DeadlockEvent
                    {
                        Type = "Grant",
                        ProcessId = processId,
                        Spid = spid,
                        ResourceId = resourceId,
                        LockMode = lockMode,
                        Description = $"进程 SPID={spid} 获得了资源 {resourceId} 上的 {lockMode} 锁"
                    });
                }

                foreach (XElement waiter in resource.Element("waiter-list")?.Elements("waiter")
                             ?? Enumerable.Empty<XElement>())
                {
                    string processId = waiter.Attribute("id")?.Value ?? string.Empty;
                    string lockMode = waiter.Attribute("mode")?.Value ?? string.Empty;
                    resourceToWaiters[resourceId].Add(processId);
                    if (!adjacency.TryGetValue(processId, out List<string>? owners))
                    {
                        owners = new List<string>();
                        adjacency[processId] = owners;
                    }
                    owners.AddRange(resourceToOwners[resourceId]);

                    string spid = GetSpid(result, processId);
                    requests.Add(new DeadlockEvent
                    {
                        Type = "Request",
                        ProcessId = processId,
                        Spid = spid,
                        ResourceId = resourceId,
                        LockMode = lockMode,
                        Description = $"进程 SPID={spid} 请求资源 {resourceId} 上的 {lockMode} 锁"
                    });
                }
            }

            HashSet<string> cycleNodes = DetectCycleNodes(result.Processes.Keys, adjacency);
            foreach (DeadlockNodeInfo process in result.Processes.Values)
            {
                process.IsInCycle = cycleNodes.Contains(process.Id);
            }
            foreach (DeadlockResourceInfo resource in result.Resources.Values)
            {
                resource.IsInCycle =
                    resourceToWaiters[resource.Id].Any(cycleNodes.Contains)
                    && resourceToOwners[resource.Id].Any(cycleNodes.Contains);
            }

            int step = 1;
            foreach (DeadlockEvent grant in grants.OrderBy(item => item.Spid))
            {
                grant.StepNumber = step++;
                grant.IsInCycle = cycleNodes.Contains(grant.ProcessId);
                result.Events.Add(grant);
            }

            foreach (DeadlockEvent request in requests.OrderBy(item => item.Spid))
            {
                request.StepNumber = step++;
                request.IsInCycle = cycleNodes.Contains(request.ProcessId);
                var blockingSpids = resourceToOwners[request.ResourceId]
                    .Select(ownerId => GetSpid(result, ownerId))
                    .ToList();
                if (blockingSpids.Count > 0)
                {
                    request.Description += $"，被进程 {string.Join(", ", blockingSpids)} 阻塞";
                }
                result.Events.Add(request);
            }

            if (victimId.Length > 0 && result.Processes.TryGetValue(victimId, out DeadlockNodeInfo? victim))
            {
                result.Events.Add(new DeadlockEvent
                {
                    StepNumber = step,
                    Type = "Victim",
                    ProcessId = victimId,
                    Spid = victim.Spid,
                    Description = $"进程 SPID={victim.Spid} 被选为死锁牺牲品",
                    IsInCycle = cycleNodes.Contains(victimId),
                    IsVictim = true
                });
            }

            return result;
        }

        private static string GetSpid(ParsedDeadlock parsed, string processId)
        {
            return parsed.Processes.TryGetValue(processId, out DeadlockNodeInfo? process)
                ? process.Spid
                : processId;
        }

        private static HashSet<string> DetectCycleNodes(
            IEnumerable<string> processIds,
            IReadOnlyDictionary<string, List<string>> adjacency)
        {
            var visited = new HashSet<string>();
            var inStack = new HashSet<string>();
            var cycleNodes = new HashSet<string>();

            void Visit(string node, List<string> path)
            {
                if (inStack.Contains(node))
                {
                    int cycleStart = path.IndexOf(node);
                    for (int i = Math.Max(cycleStart, 0); i < path.Count; i++)
                    {
                        cycleNodes.Add(path[i]);
                    }
                    return;
                }
                if (!visited.Add(node))
                {
                    return;
                }

                inStack.Add(node);
                path.Add(node);
                if (adjacency.TryGetValue(node, out List<string>? neighbors))
                {
                    foreach (string neighbor in neighbors)
                    {
                        Visit(neighbor, path);
                    }
                }
                path.RemoveAt(path.Count - 1);
                inStack.Remove(node);
            }

            foreach (string processId in processIds)
            {
                Visit(processId, new List<string>());
            }

            return cycleNodes;
        }
    }
}
