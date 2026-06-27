using System;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Utilities;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class MermaidDiagramServiceTests
    {
        private static readonly XNamespace ShowplanNs =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void BuildDeadlockDiagram_ReturnsWaitGraphMermaid()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "deadlock_bookmark_lookup.xdl");
            var document = XDocument.Parse(xml);
            var service = new MermaidDiagramService();

            string mermaid = service.BuildDeadlockDiagram(document);

            mermaid.Should().Contain("flowchart TD");
            mermaid.Should().Contain("process1");
            mermaid.Should().Contain("process2");
            mermaid.Should().Contain(":::victim");
        }

        [Fact]
        public void BuildDeadlockDiagram_WhenDocumentIsInvalid_Throws()
        {
            var document = XDocument.Parse("<deadlock></deadlock>");
            var service = new MermaidDiagramService();

            Action act = () => service.BuildDeadlockDiagram(document);

            act.Should().Throw<InvalidDataException>();
        }

        [Fact]
        public void BuildPlanDiagram_ReturnsExecutionPlanMermaid()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "plan_missing_index.sqlplan");
            var document = XDocument.Parse(xml);
            var service = new MermaidDiagramService();

            string mermaid = service.BuildPlanDiagram(document, ShowplanNs);

            mermaid.Should().Contain("flowchart TD");
        }

        [Fact]
        public void BuildPlanDiagram_WhenDocumentIsNull_Throws()
        {
            var service = new MermaidDiagramService();

            Action act = () => service.BuildPlanDiagram(null!, ShowplanNs);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
