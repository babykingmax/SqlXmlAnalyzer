using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Parsers;

public class DeadlockTimelineParser
{
    public class ParsedDeadlock
    {
        public List<DeadlockEvent> Events { get; set; } = new();
        public Dictionary<string, DeadlockNodeInfo> Processes { get; set; } = new();
        public Dictionary<string, DeadlockResourceInfo> Resources { get; set; } = new();
        public DeadlockGraph? Graph { get; init; }
        public string CycleSummary => Graph?.CycleAnalysis.Summary ?? "";
    }

    public DeadlockParseResult<ParsedDeadlock> ParseResult(string xmlContent,
        CancellationToken cancellationToken = default, DeadlockGraphOptions? options = null,
        IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(xmlContent)) return DeadlockParseResult<ParsedDeadlock>.Failure("死锁 XML 内容为空。");
        try
        {
            var parsed = DeadlockXmlParser.TryParseDeadlockXml(
                SafeXmlHelper.ParseSafe(xmlContent, new Services.DocumentReadOptions(), cancellationToken), cancellationToken, unexpectedErrors);
            if (!parsed.IsSuccess || parsed.Value == null) return new(null, parsed.Errors, parsed.Warnings);
            var graph = DeadlockGraphBuilder.Build(parsed.Value, options, cancellationToken, unexpectedErrors);
            return DeadlockParseResult<ParsedDeadlock>.Success(FromGraph(graph, cancellationToken), parsed.Warnings.Concat(graph.Warnings).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return DeadlockParseResult<ParsedDeadlock>.Failure(ExceptionPolicy.Describe(ex, "DeadlockTimelineParser.ParseResult", unexpectedErrors));
        }
    }

    [Obsolete("Use ParseResult and inspect IsSuccess, Errors, and Warnings.")]
    public ParsedDeadlock Parse(string xmlContent) => ParseResult(xmlContent).Value ?? new ParsedDeadlock();

    /// <summary>Dependency explanation order, not an observed event chronology.</summary>
    public static ParsedDeadlock FromGraph(DeadlockGraph graph, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new ParsedDeadlock { Graph = graph };
        var cycleResources = graph.ResourceLinks.Where(l => graph.CycleAnalysis.LinkIds.Contains(l.LinkId))
            .Select(l => l.ResourceId).ToHashSet(StringComparer.Ordinal);
        foreach (var process in graph.Processes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Processes.Add(process.Id, new DeadlockNodeInfo { Id = process.Id, Spid = process.Spid,
                IsInCycle = graph.CycleAnalysis.ProcessIds.Contains(process.Id), IsVictim = graph.VictimProcessIds.Contains(process.Id) });
        }
        foreach (var resource in graph.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Resources.Add(resource.Id, new DeadlockResourceInfo { Id = resource.Id, Name = resource.LockType,
                IsInCycle = cycleResources.Contains(resource.Id) });
        }
        foreach (var link in graph.ResourceLinks.OrderBy(l => l.IsWaiter).ThenBy(l => l.LinkId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string spid = result.Processes[link.ProcessId].Spid;
            result.Events.Add(new DeadlockEvent { StepNumber = result.Events.Count + 1, Type = link.IsWaiter ? "Request" : "Grant",
                ProcessId = link.ProcessId, Spid = spid, ResourceId = link.ResourceId, LockMode = link.Mode,
                EvidenceId = link.LinkId, Source = link.Source, IsInCycle = graph.CycleAnalysis.LinkIds.Contains(link.LinkId),
                Description = $"捕获关系：SPID={spid} {(link.IsWaiter ? "等待" : "持有")}资源 {link.ResourceId}，模式={link.Mode}，requestType={link.RequestType}" });
        }
        foreach (var victim in result.Processes.Values.Where(p => p.IsVictim).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Events.Add(new DeadlockEvent { StepNumber = result.Events.Count + 1, Type = "Victim", ProcessId = victim.Id,
                Spid = victim.Spid, IsVictim = true, IsInCycle = victim.IsInCycle, Description = $"victim-list 指定 SPID={victim.Spid} 为受害者" });
        }
        Logger.Debug($"DeadlockTimelineParser.FromGraph: events={result.Events.Count}");
        return result;
    }
}
