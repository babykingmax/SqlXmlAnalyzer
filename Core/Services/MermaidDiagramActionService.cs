using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum MermaidDiagramActionStatus
    {
        Ready,
        MissingDocument
    }

    public sealed record MermaidDiagramActionResult(
        MermaidDiagramActionStatus Status,
        string MermaidCode,
        string UserMessage,
        string LogMessage);

    public sealed class MermaidDiagramActionService
    {
        private readonly MermaidDiagramService _mermaidDiagramService;

        public MermaidDiagramActionService(MermaidDiagramService? mermaidDiagramService = null)
        {
            _mermaidDiagramService = mermaidDiagramService ?? new MermaidDiagramService();
        }

        public MermaidDiagramActionResult BuildDeadlockDiagram(XDocument? document)
        {
            if (document == null)
            {
                return new MermaidDiagramActionResult(
                    MermaidDiagramActionStatus.MissingDocument,
                    string.Empty,
                    "No deadlock document is loaded.",
                    string.Empty);
            }

            return new MermaidDiagramActionResult(
                MermaidDiagramActionStatus.Ready,
                _mermaidDiagramService.BuildDeadlockDiagram(document),
                string.Empty,
                "Generated deadlock Mermaid diagram.");
        }

        public MermaidDiagramActionResult BuildPlanDiagram(
            XDocument? document,
            XNamespace showplanNamespace)
        {
            ArgumentNullException.ThrowIfNull(showplanNamespace);

            if (document == null)
            {
                return new MermaidDiagramActionResult(
                    MermaidDiagramActionStatus.MissingDocument,
                    string.Empty,
                    "No execution plan document is loaded.",
                    string.Empty);
            }

            return new MermaidDiagramActionResult(
                MermaidDiagramActionStatus.Ready,
                _mermaidDiagramService.BuildPlanDiagram(document, showplanNamespace),
                string.Empty,
                "Generated execution plan Mermaid diagram.");
        }
    }
}
