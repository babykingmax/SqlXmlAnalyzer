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
            public List<DeadlockEvent> Events { get; set; } = new List<DeadlockEvent>();
            public Dictionary<string, DeadlockNodeInfo> Processes { get; set; } = new Dictionary<string, DeadlockNodeInfo>();
            public Dictionary<string, DeadlockResourceInfo> Resources { get; set; } = new Dictionary<string, DeadlockResourceInfo>();
        }

        public ParsedDeadlock Parse(string xmlContent)
        {
            var result = new ParsedDeadlock();
            try
            {
                var doc = XDocument.Parse(xmlContent);
                var deadlockNode = doc.Descendants("deadlock").FirstOrDefault();
                if (deadlockNode == null) return result;

                var victimId = deadlockNode.Element("victim-list")?.Element("victimProcess")?.Attribute("id")?.Value ?? "";

                // 1. Parse Processes
                var processList = deadlockNode.Element("process-list");
                if (processList != null)
                {
                    foreach (var proc in processList.Elements("process"))
                    {
                        var id = proc.Attribute("id")?.Value ?? "";
                        var spid = proc.Attribute("spid")?.Value ?? "";
                        result.Processes[id] = new DeadlockNodeInfo
                        {
                            Id = id,
                            Spid = spid,
                            IsVictim = id == victimId
                        };
                    }
                }

                // 2. Parse Resources and wait graph
                var resourceList = deadlockNode.Element("resource-list");
                var adjList = new Dictionary<string, List<string>>(); // Waiter -> Owner
                var resourceToWaiters = new Dictionary<string, List<string>>();
                var resourceToOwners = new Dictionary<string, List<string>>();

                var grants = new List<DeadlockEvent>();
                var requests = new List<DeadlockEvent>();

                if (resourceList != null)
                {
                    int resIndex = 0;
                    foreach (var res in resourceList.Elements())
                    {
                        var resId = $"res_{resIndex++}";
                        var resName = res.Name.LocalName;
                        result.Resources[resId] = new DeadlockResourceInfo { Id = resId, Name = resName };

                        resourceToWaiters[resId] = new List<string>();
                        resourceToOwners[resId] = new List<string>();

                        var ownerList = res.Element("owner-list");
                        if (ownerList != null)
                        {
                            foreach (var owner in ownerList.Elements("owner"))
                            {
                                var pid = owner.Attribute("id")?.Value ?? "";
                                var mode = owner.Attribute("mode")?.Value ?? "";
                                resourceToOwners[resId].Add(pid);
                                
                                var spid = result.Processes.ContainsKey(pid) ? result.Processes[pid].Spid : pid;
                                grants.Add(new DeadlockEvent
                                {
                                    Type = "Grant",
                                    ProcessId = pid,
                                    Spid = spid,
                                    ResourceId = resId,
                                    LockMode = mode,
                                    Description = $"进程 SPID={spid} 获得了资源 {resId} 上的 {mode} 锁"
                                });
                            }
                        }

                        var waiterList = res.Element("waiter-list");
                        if (waiterList != null)
                        {
                            foreach (var waiter in waiterList.Elements("waiter"))
                            {
                                var pid = waiter.Attribute("id")?.Value ?? "";
                                var mode = waiter.Attribute("mode")?.Value ?? "";
                                resourceToWaiters[resId].Add(pid);

                                if (!adjList.ContainsKey(pid)) adjList[pid] = new List<string>();
                                foreach (var owner in resourceToOwners[resId])
                                {
                                    adjList[pid].Add(owner);
                                }

                                var spid = result.Processes.ContainsKey(pid) ? result.Processes[pid].Spid : pid;
                                requests.Add(new DeadlockEvent
                                {
                                    Type = "Request",
                                    ProcessId = pid,
                                    Spid = spid,
                                    ResourceId = resId,
                                    LockMode = mode,
                                    Description = $"进程 SPID={spid} 请求资源 {resId} 上的 {mode} 锁"
                                });
                            }
                        }
                    }
                }

                // 3. Detect Cycle (DFS)
                var visited = new HashSet<string>();
                var inStack = new HashSet<string>();
                var cycleNodes = new HashSet<string>();

                bool DFS(string node, List<string> path)
                {
                    if (inStack.Contains(node))
                    {
                        var cycleStart = path.IndexOf(node);
                        for (int i = cycleStart; i < path.Count; i++) cycleNodes.Add(path[i]);
                        return true;
                    }
                    if (visited.Contains(node)) return false;

                    visited.Add(node);
                    inStack.Add(node);
                    path.Add(node);

                    bool foundCycle = false;
                    if (adjList.ContainsKey(node))
                    {
                        foreach (var neighbor in adjList[node])
                        {
                            if (DFS(neighbor, path)) foundCycle = true;
                        }
                    }

                    path.RemoveAt(path.Count - 1);
                    inStack.Remove(node);
                    return foundCycle;
                }

                foreach (var node in result.Processes.Keys)
                {
                    if (!visited.Contains(node)) DFS(node, new List<string>());
                }

                // Mark cycle
                foreach (var p in result.Processes.Values)
                {
                    if (cycleNodes.Contains(p.Id)) p.IsInCycle = true;
                }
                foreach (var r in result.Resources.Values)
                {
                    bool resourceInCycle = false;
                    if (resourceToWaiters.TryGetValue(r.Id, out var waiters) && resourceToOwners.TryGetValue(r.Id, out var owners))
                    {
                        if (waiters.Any(w => cycleNodes.Contains(w)) && owners.Any(o => cycleNodes.Contains(o)))
                        {
                            resourceInCycle = true;
                        }
                    }
                    r.IsInCycle = resourceInCycle;
                }

                // 4. Assemble Timeline
                int step = 1;
                foreach (var g in grants.OrderBy(x => x.Spid))
                {
                    g.StepNumber = step++;
                    g.IsInCycle = cycleNodes.Contains(g.ProcessId);
                    result.Events.Add(g);
                }

                foreach (var r in requests.OrderBy(x => x.Spid))
                {
                    r.StepNumber = step++;
                    r.IsInCycle = cycleNodes.Contains(r.ProcessId);
                    
                    // Add blocked info to description
                    var blockingOwners = resourceToOwners.ContainsKey(r.ResourceId) ? resourceToOwners[r.ResourceId] : new List<string>();
                    if (blockingOwners.Any())
                    {
                        var blockingSpids = blockingOwners.Select(o => result.Processes.ContainsKey(o) ? result.Processes[o].Spid : o).ToList();
                        r.Description += $"，被进程 {string.Join(", ", blockingSpids)} 阻塞";
                    }
                    result.Events.Add(r);
                }

                if (!string.IsNullOrEmpty(victimId) && result.Processes.ContainsKey(victimId))
                {
                    var vp = result.Processes[victimId];
                    result.Events.Add(new DeadlockEvent
                    {
                        StepNumber = step++,
                        Type = "Victim",
                        ProcessId = victimId,
                        Spid = vp.Spid,
                        Description = $"进程 SPID={vp.Spid} 被选为死锁牺牲品",
                        IsInCycle = cycleNodes.Contains(victimId),
                        IsVictim = true
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("DeadlockTimelineParser.ParseDeadlockTimeline", ex);
            }
            return result;
        }
    }
}
