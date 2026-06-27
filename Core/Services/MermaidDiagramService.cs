using System;
using System.IO;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class MermaidDiagramService
    {
        public string BuildDeadlockDiagram(
            XDocument document,
            bool includeLegend = true)
        {
            ArgumentNullException.ThrowIfNull(document);

            var parseResult = DeadlockXmlParser.TryParseDeadlockXml(document);
            if (!parseResult.IsSuccess || parseResult.Value == null)
            {
                throw new InvalidDataException(
                    string.Join(Environment.NewLine, parseResult.Errors));
            }

            var parsed = parseResult.Value;
            var graph = DeadlockGraphBuilder.Build(
                parsed.Processes,
                parsed.Resources,
                parsed.VictimId);

            return BuildDeadlockDiagram(graph, includeLegend);
        }

        public string BuildDeadlockDiagram(
            DeadlockGraph graph,
            bool includeLegend = true)
        {
            ArgumentNullException.ThrowIfNull(graph);

            return DeadlockGraphBuilder.GenerateMermaid(graph, includeLegend);
        }

        public string BuildPlanDiagram(
            XDocument document,
            XNamespace showplanNamespace)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(showplanNamespace);

            return ExecutionPlanVisualizer.GenerateMermaidPlan(
                document,
                showplanNamespace);
        }
    }
}
