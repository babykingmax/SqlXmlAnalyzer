using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer;

public sealed record DeadlockGraphOptions
{
    public int MaxWaitForEdges { get; init; } = 100_000;
    public int MaxResourceLinks { get; init; } = 200_000;
    public int MaxCyclePaths { get; init; } = 64;
    public int MaxCycleLength { get; init; } = 128;
    public int MaxCycleSearchSteps { get; init; } = 100_000;

    public void Validate()
    {
        if (MaxWaitForEdges is < 1 or > 1_000_000 || MaxResourceLinks is < 1 or > 1_000_000
            || MaxCyclePaths is < 1 or > 10_000 || MaxCycleLength is < 1 or > 4096
            || MaxCycleSearchSteps is < 1 or > 10_000_000)
            throw new ArgumentOutOfRangeException(nameof(DeadlockGraphOptions), "死锁图预算超出允许范围。");
    }
}

public sealed record DeadlockResourceLink(string LinkId, string ProcessId, string ResourceId,
    bool IsWaiter, string Mode, string RequestType, SourceLocation? Source)
{
    public IReadOnlyDictionary<string, string> RawFields { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}

public sealed record DeadlockEvidence(string Name, string Value, string? ProcessId = null,
    string? ResourceId = null, string? EdgeId = null, SourceLocation? Source = null);

public sealed class DeadlockCycleAnalysis
{
    internal static DeadlockCycleAnalysis Empty { get; } = new([], [], [], [], false, 0, []);
    internal DeadlockCycleAnalysis(IEnumerable<IReadOnlyList<string>> components, IEnumerable<string> processes,
        IEnumerable<string> edges, IEnumerable<string> links, bool truncated, int steps, IEnumerable<string> reasons)
    {
        Components = Array.AsReadOnly(components.ToArray());
        ProcessIds = processes.ToFrozenSet(StringComparer.Ordinal);
        EdgeIds = edges.ToFrozenSet(StringComparer.Ordinal);
        LinkIds = links.ToFrozenSet(StringComparer.Ordinal);
        PathsTruncated = truncated; SearchSteps = steps;
        TruncationReasons = Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal).ToArray());
    }
    public IReadOnlyList<IReadOnlyList<string>> Components { get; }
    public IReadOnlySet<string> ProcessIds { get; }
    public IReadOnlySet<string> EdgeIds { get; }
    public IReadOnlySet<string> LinkIds { get; }
    public bool PathsTruncated { get; }
    public int SearchSteps { get; }
    public IReadOnlyList<string> TruncationReasons { get; }
    public string Summary => $"等待图：{ProcessIds.Count} 个环成员；{Components.Count(c => c.Count > 1)} 个循环强连通分量。"
        + (PathsTruncated ? "解释环路径已截断（" + string.Join("、", TruncationReasons) + "）；成员判定仍使用完整等待图。" : "解释路径在本次预算内完成。");
}

internal static class DeadlockIdentity
{
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}

public static partial class DeadlockGraphBuilder
{
    public static DeadlockGraph Build(List<DeadlockProcess> processes, List<LockResource> resources, string victimProcessId,
        DeadlockGraphOptions? options = null, CancellationToken cancellationToken = default, IUnexpectedErrorReporter? unexpectedErrors = null) =>
        Build(new ParsedDeadlockGraphData(processes, resources, victimProcessId)
        { VictimIds = string.IsNullOrEmpty(victimProcessId) ? new HashSet<string>() : new HashSet<string> { victimProcessId } },
            options, cancellationToken, unexpectedErrors);

    public static DeadlockGraph Build(ParsedDeadlockGraphData parsed, DeadlockGraphOptions? options = null,
        CancellationToken cancellationToken = default, IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        options ??= new();
        options.Validate();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.Debug("IMP-13: 开始构建共享死锁等待图。");
            var graph = new DeadlockGraph { VictimProcessId = parsed.VictimId ?? "",
                VictimProcessIds = parsed.VictimIds.Concat(string.IsNullOrEmpty(parsed.VictimId) ? [] : new[] { parsed.VictimId }).ToFrozenSet(StringComparer.Ordinal),
                SourceFingerprint = parsed.SourceFingerprint };
            graph.Processes.AddRange(parsed.Processes);
            graph.Resources.AddRange(parsed.Resources);
            var processIds = graph.Processes.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            if (processIds.Count != graph.Processes.Count || processIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("DEADLOCK_PROCESS_ID_INVALID: 进程编号缺失或重复。");
            if (graph.Resources.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != graph.Resources.Count)
                throw new InvalidDataException("DEADLOCK_RESOURCE_ID_INVALID: 资源内部编号重复。");
            var warnings = new HashSet<string>(StringComparer.Ordinal);
            int pairs = 0;
            foreach (var resource in graph.Resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = string.IsNullOrEmpty(resource.ResourceKey) ? DeadlockIdentity.Hash(new { resource.Id, resource.LockType, resource.ObjectName }) : resource.ResourceKey;
                var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
                DeadlockResourceLink? Link(string process, bool waiter, string mode, string request, SourceLocation? source, IReadOnlyDictionary<string, string> rawFields)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!processIds.Contains(process)) { warnings.Add("DEADLOCK_UNKNOWN_PROCESS: owner/waiter 引用了未定义进程；原字段保留，未构建该依赖。"); return null; }
                    if (graph.ResourceLinks.Count >= options.MaxResourceLinks) throw new DocumentBudgetExceededException("DeadlockResourceLinks");
                    var fields = new ReadOnlyDictionary<string, string>(rawFields.OrderBy(f => f.Key, StringComparer.Ordinal)
                        .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal));
                    string identity = DeadlockIdentity.Hash(new { key, process, waiter, mode, request, fields });
                    int occurrence = occurrences.GetValueOrDefault(identity);
                    occurrences[identity] = occurrence + 1;
                    var link = new DeadlockResourceLink(identity + ":" + occurrence, process, resource.Id, waiter, mode, request, source ?? resource.Source) { RawFields = fields };
                    graph.ResourceLinks.Add(link);
                    return link;
                }
                var owners = resource.Owners.Select(o => Link(o.Id, false, o.Mode, "", o.Source, o.RawFields)).OfType<DeadlockResourceLink>().ToArray();
                var waiters = resource.Waiters.Select(w => Link(w.Id, true, w.Mode, w.RequestType, w.Source, w.RawFields)).OfType<DeadlockResourceLink>().ToArray();
                foreach (var waiter in waiters)
                foreach (var owner in owners)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++pairs > options.MaxWaitForEdges) throw new DocumentBudgetExceededException("DeadlockWaitForEdges");
                    if (waiter.ProcessId == owner.ProcessId)
                    {
                        warnings.Add("DEADLOCK_SELF_REFERENCE: 同一进程的 owner/waiter 不足以证明自阻塞；保留原关系，未将其作为自环。");
                        continue;
                    }
                    graph.Edges.Add(new WaitForEdge { FromProcessId = waiter.ProcessId, ToProcessId = owner.ProcessId, Resource = resource,
                        RequestedMode = waiter.Mode, HeldMode = owner.Mode, WaiterLinkId = waiter.LinkId, OwnerLinkId = owner.LinkId,
                        EdgeId = DeadlockIdentity.Hash(new[] { waiter.LinkId, owner.LinkId }) });
                }
            }
            graph.CycleAnalysis = AnalyzeCycles(graph, options, cancellationToken);
            if (graph.CycleAnalysis.PathsTruncated) warnings.Add(graph.CycleAnalysis.Summary);
            graph.Warnings = Array.AsReadOnly(warnings.Order(StringComparer.Ordinal).ToArray());
            if (graph.Warnings.Count > 0) Logger.Warning($"IMP-13: 图分析发现 {graph.Warnings.Count} 类警告；详情保留在分析结果中。");
            Logger.Debug($"IMP-13: 共享图完成；进程 {graph.Processes.Count}、依赖 {graph.Edges.Count}、环成员 {graph.CycleAnalysis.ProcessIds.Count}、解释路径 {graph.Cycles.Count}。");
            return graph;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Logger.Warning("IMP-13: 死锁图分析已取消。"); throw; }
        catch (Exception exception) { ExceptionPolicy.Describe(exception, "IMP13.DeadlockGraphBuilder", unexpectedErrors); throw; }
    }

    private static DeadlockCycleAnalysis AnalyzeCycles(DeadlockGraph graph, DeadlockGraphOptions options, CancellationToken cancellationToken)
    {
        var nodes = graph.Processes.Select(p => p.Id).Order(StringComparer.Ordinal).ToArray();
        var adjacency = nodes.ToDictionary(n => n, _ => new List<string>(), StringComparer.Ordinal);
        var reverse = nodes.ToDictionary(n => n, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            adjacency[edge.FromProcessId].Add(edge.ToProcessId); reverse[edge.ToProcessId].Add(edge.FromProcessId);
        }
        // Iterative Kosaraju: SCC membership is complete and independent of path-enumeration budgets.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (string node in nodes)
        {
            if (!visited.Add(node)) continue;
            var stack = new Stack<(string Node, int Next)>(); stack.Push((node, 0));
            while (stack.TryPop(out var frame))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (frame.Next == adjacency[frame.Node].Count) { order.Add(frame.Node); continue; }
                stack.Push((frame.Node, frame.Next + 1));
                string neighbor = adjacency[frame.Node][frame.Next];
                if (visited.Add(neighbor)) stack.Push((neighbor, 0));
            }
        }
        visited.Clear();
        var components = new List<IReadOnlyList<string>>();
        var componentOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string node in order.AsEnumerable().Reverse())
        {
            if (!visited.Add(node)) continue;
            var members = new List<string>();
            var stack = new Stack<string>(); stack.Push(node);
            while (stack.TryPop(out string? current))
            {
                cancellationToken.ThrowIfCancellationRequested(); members.Add(current);
                foreach (string neighbor in reverse[current]) if (visited.Add(neighbor)) stack.Push(neighbor);
            }
            members.Sort(StringComparer.Ordinal);
            foreach (string member in members) componentOf[member] = components.Count;
            components.Add(members.AsReadOnly());
        }
        var cyclicEdges = graph.Edges.Where(e => componentOf[e.FromProcessId] == componentOf[e.ToProcessId]
            && components[componentOf[e.FromProcessId]].Count > 1).ToArray();
        var cyclicNodes = components.Where(c => c.Count > 1).SelectMany(c => c).ToHashSet(StringComparer.Ordinal);
        var cycleAdjacency = nodes.ToDictionary(n => n, _ => new List<WaitForEdge>(), StringComparer.Ordinal);
        foreach (var edge in cyclicEdges) cycleAdjacency[edge.FromProcessId].Add(edge);
        foreach (var edges in cycleAdjacency.Values) edges.Sort((a, b) => string.CompareOrdinal(a.EdgeId, b.EdgeId));
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var cycleIds = new HashSet<string>(StringComparer.Ordinal);
        int steps = 0;
        bool stop = false;
        foreach (string start in nodes.Where(cyclicNodes.Contains))
        {
            if (stop) break;
            var path = new List<string> { start };
            var pathEdges = new List<WaitForEdge>();
            var onPath = new HashSet<string>(StringComparer.Ordinal) { start };
            var positions = new List<int> { 0 };
            while (path.Count > 0 && !stop)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = path[^1];
                if (positions[^1] == cycleAdjacency[current].Count)
                {
                    path.RemoveAt(path.Count - 1); positions.RemoveAt(positions.Count - 1); onPath.Remove(current);
                    if (pathEdges.Count > 0) pathEdges.RemoveAt(pathEdges.Count - 1);
                    continue;
                }
                if (steps >= options.MaxCycleSearchSteps) { reasons.Add("MaxCycleSearchSteps"); stop = true; break; }
                steps++;
                var edge = cycleAdjacency[current][positions[^1]++];
                string next = edge.ToProcessId;
                // Only the lexicographically smallest member starts each directed cycle.
                if (string.CompareOrdinal(next, start) < 0) continue;
                if (next == start)
                {
                    var cycle = NormalizeCycle(path, pathEdges.Append(edge));
                    if (!cycleIds.Add(cycle.CycleId)) continue;
                    if (graph.Cycles.Count == options.MaxCyclePaths) { reasons.Add("MaxCyclePaths"); stop = true; break; }
                    graph.Cycles.Add(cycle);
                }
                else if (!onPath.Contains(next))
                {
                    if (path.Count >= options.MaxCycleLength) { reasons.Add("MaxCycleLength"); continue; }
                    path.Add(next); pathEdges.Add(edge); positions.Add(0); onPath.Add(next);
                }
            }
        }
        graph.Cycles.Sort((a, b) => string.CompareOrdinal(a.CycleId, b.CycleId));
        return new(components.OrderBy(c => c[0], StringComparer.Ordinal), cyclicNodes, cyclicEdges.Select(e => e.EdgeId),
            cyclicEdges.SelectMany(e => new[] { e.WaiterLinkId, e.OwnerLinkId }), reasons.Count > 0, steps, reasons.Order(StringComparer.Ordinal));
    }

    public static DeadlockCycle NormalizeCycle(IEnumerable<string> processIds, IEnumerable<WaitForEdge> edges)
    {
        var nodes = processIds.ToList(); var links = edges.ToList();
        if (nodes.Count > 1 && nodes[0] == nodes[^1]) nodes.RemoveAt(nodes.Count - 1);
        if (nodes.Count == 0 || nodes.Count != links.Count) throw new ArgumentException("环路径的进程数与边数不匹配。");
        int first = Enumerable.Range(0, nodes.Count).MinBy(i => nodes[i], StringComparer.Ordinal);
        var normalizedNodes = nodes.Skip(first).Concat(nodes.Take(first)).ToList();
        var normalizedEdges = links.Skip(first).Concat(links.Take(first)).ToList();
        return new DeadlockCycle { ProcessIds = normalizedNodes, EdgesInCycle = normalizedEdges,
            CycleId = DeadlockIdentity.Hash(new { Nodes = normalizedNodes, Edges = normalizedEdges.Select(e => e.EdgeId).ToArray() }) };
    }
}
