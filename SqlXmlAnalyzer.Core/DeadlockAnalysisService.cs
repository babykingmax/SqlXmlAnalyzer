using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.Core;

public sealed record DeadlockAnalysisOutput(List<DeadlockProcess> Processes, List<LockResource> Resources,
    DeadlockGraph Graph, List<DeadlockPattern> Patterns, string Mermaid,
    DeadlockTimelineParser.ParsedDeadlock Timeline, IReadOnlyList<string> Warnings);

public sealed class DeadlockAnalysisService
{
    private readonly IUnexpectedErrorReporter? _unexpectedErrors;
    private readonly DeadlockGraphOptions _options;
    private readonly Func<ParsedDeadlockGraphData, DeadlockGraphOptions, CancellationToken, DeadlockGraph> _graphBuilder;

    public DeadlockAnalysisService(IUnexpectedErrorReporter? unexpectedErrors = null, DeadlockGraphOptions? options = null,
        Func<ParsedDeadlockGraphData, DeadlockGraphOptions, CancellationToken, DeadlockGraph>? graphBuilder = null)
    {
        _unexpectedErrors = unexpectedErrors; _options = options ?? new(); _options.Validate();
        _graphBuilder = graphBuilder ?? ((parsed, limits, token) => DeadlockGraphBuilder.Build(parsed, limits, token, unexpectedErrors));
    }

    public DeadlockAnalysisOutput Analyze(XDocument document, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.Debug("IMP-13: 开始死锁事件分析。");
            var parsed = DeadlockXmlParser.TryParseDeadlockXml(document, cancellationToken, _unexpectedErrors);
            if (!parsed.IsSuccess || parsed.Value == null) throw new InvalidDataException(string.Join(Environment.NewLine, parsed.Errors));
            var graph = _graphBuilder(parsed.Value, _options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);
            var mermaid = DeadlockGraphBuilder.GenerateMermaid(graph, true);
            var timeline = DeadlockTimelineParser.FromGraph(graph, cancellationToken);
            Logger.Debug($"IMP-13: 死锁分析完成，processes={graph.Processes.Count}, resources={graph.Resources.Count}, diagnostics={patterns.Count}");
            return new(graph.Processes, graph.Resources, graph, patterns, mermaid, timeline,
                parsed.Warnings.Concat(graph.Warnings).Distinct().ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Warning("DeadlockAnalysisService: 分析已取消。");
            throw;
        }
        catch (Exception ex)
        {
            ExceptionPolicy.Describe(ex, "DeadlockAnalysisService.Analyze", _unexpectedErrors);
            throw;
        }
    }
}
