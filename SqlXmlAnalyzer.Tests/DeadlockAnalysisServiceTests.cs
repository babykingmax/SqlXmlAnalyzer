using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Tests.Utilities;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class DeadlockAnalysisServiceTests
    {
        [Fact]
        public void Analyze_WhenDeadlockIsValid_ReturnsCompleteAnalysis()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "deadlock_bookmark_lookup.xdl");
            var document = XDocument.Parse(xml);
            var service = new DeadlockAnalysisService();

            DeadlockAnalysisOutput result = service.Analyze(document);

            result.Processes.Should().HaveCount(2);
            result.Resources.Should().HaveCount(2);
            result.Graph.Should().NotBeNull();
            result.Patterns.Should().NotBeNull();
            result.Mermaid.Should().StartWith("flowchart");
            result.Timeline.Events.Should().NotBeEmpty();
        }
    }
}
