using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Utilities;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class MermaidDiagramActionServiceTests
    {
        private static readonly XNamespace ShowplanNs =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void BuildDeadlockDiagram_WhenDocumentIsMissing_ReturnsMissingDocumentStatus()
        {
            var service = new MermaidDiagramActionService();

            MermaidDiagramActionResult result = service.BuildDeadlockDiagram(null);

            result.Status.Should().Be(MermaidDiagramActionStatus.MissingDocument);
            result.MermaidCode.Should().BeEmpty();
            result.UserMessage.Should().Be("No deadlock document is loaded.");
        }

        [Fact]
        public void BuildDeadlockDiagram_WhenDocumentExists_ReturnsMermaidCode()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "deadlock_bookmark_lookup.xdl");
            var document = XDocument.Parse(xml);
            var service = new MermaidDiagramActionService();

            MermaidDiagramActionResult result = service.BuildDeadlockDiagram(document);

            result.Status.Should().Be(MermaidDiagramActionStatus.Ready);
            result.MermaidCode.Should().Contain("flowchart TD");
            result.LogMessage.Should().Be("Generated deadlock Mermaid diagram.");
        }

        [Fact]
        public void BuildPlanDiagram_WhenDocumentIsMissing_ReturnsMissingDocumentStatus()
        {
            var service = new MermaidDiagramActionService();

            MermaidDiagramActionResult result = service.BuildPlanDiagram(null, ShowplanNs);

            result.Status.Should().Be(MermaidDiagramActionStatus.MissingDocument);
            result.MermaidCode.Should().BeEmpty();
            result.UserMessage.Should().Be("No execution plan document is loaded.");
        }

        [Fact]
        public void BuildPlanDiagram_WhenDocumentExists_ReturnsMermaidCode()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "plan_missing_index.sqlplan");
            var document = XDocument.Parse(xml);
            var service = new MermaidDiagramActionService();

            MermaidDiagramActionResult result = service.BuildPlanDiagram(document, ShowplanNs);

            result.Status.Should().Be(MermaidDiagramActionStatus.Ready);
            result.MermaidCode.Should().Contain("flowchart TD");
            result.LogMessage.Should().Be("Generated execution plan Mermaid diagram.");
        }
    }
}
