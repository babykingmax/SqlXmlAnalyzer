using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.Core
{
    public sealed record DeadlockAnalysisOutput(
        List<DeadlockProcess> Processes,
        List<LockResource> Resources,
        DeadlockGraph Graph,
        List<DeadlockPattern> Patterns,
        string Mermaid,
        DeadlockTimelineParser.ParsedDeadlock Timeline,
        IReadOnlyList<string> Warnings);

    public sealed class DeadlockAnalysisService
    {
        public DeadlockAnalysisOutput Analyze(
            XDocument document,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parseResult = DeadlockXmlParser.TryParseDeadlockXml(document);
            if (!parseResult.IsSuccess || parseResult.Value == null)
            {
                throw new InvalidDataException(
                    string.Join(Environment.NewLine, parseResult.Errors));
            }

            ParsedDeadlockGraphData parsed = parseResult.Value;
            cancellationToken.ThrowIfCancellationRequested();
            DeadlockGraph graph = DeadlockGraphBuilder.Build(
                parsed.Processes,
                parsed.Resources,
                parsed.VictimId);
            List<DeadlockPattern> patterns =
                DeadlockPatternAnalyzer.IdentifyPatterns(graph, document);
            string mermaid = DeadlockGraphBuilder.GenerateMermaid(graph, true);

            cancellationToken.ThrowIfCancellationRequested();
            var timelineResult = new DeadlockTimelineParser().ParseResult(
                document.ToString());
            if (!timelineResult.IsSuccess || timelineResult.Value == null)
            {
                throw new InvalidDataException(
                    string.Join(Environment.NewLine, timelineResult.Errors));
            }

            var warnings = parseResult.Warnings
                .Concat(timelineResult.Warnings)
                .Distinct()
                .ToList();
            return new DeadlockAnalysisOutput(
                parsed.Processes,
                parsed.Resources,
                graph,
                patterns,
                mermaid,
                timelineResult.Value,
                warnings);
        }
    }
}
