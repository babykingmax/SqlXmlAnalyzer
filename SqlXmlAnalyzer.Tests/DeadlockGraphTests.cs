using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer;
using SqlXmlAnalyzer.Tests.Utilities;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class DeadlockGraphTests
    {
        [Fact]
        public void ParseDeadlockXml_ShouldReturnProcessesAndIdentifyVictim()
        {
            // Arrange
            string xmlContent = EmbeddedResourceHelper.GetResourceContent("deadlock_bookmark_lookup.xdl");
            var doc = XDocument.Parse(xmlContent);

            // Act
            var (processes, resources, victimId) = DeadlockXmlParser.ParseDeadlockXml(doc);

            // Assert
            processes.Should().NotBeNullOrEmpty();
            processes.Count.Should().Be(2);
            
            resources.Should().NotBeNullOrEmpty();
            resources.Count.Should().Be(2);

            // Check if victim ID is correctly parsed from <victimProcess>
            victimId.Should().Be("process1");

            // Verify LogUsed is extracted properly
            var p1 = processes.Find(p => p.Id == "process1");
            var p2 = processes.Find(p => p.Id == "process2");
            
            p1.Should().NotBeNull();
            p1!.LogUsed.Should().Be("100");

            p2.Should().NotBeNull();
            p2!.LogUsed.Should().Be("5000");
            
            // Verify resources parsed
            resources[0].Id.Should().Be("res_0");
            resources[0].LockType.Should().Be("keylock");
            resources[0].ObjectName.Should().Be("TestDB.dbo.TableA");
        }

        [Fact]
        public void ParseDeadlockXml_EmptyXml_ReturnsEmpty()
        {
            // Arrange
            string xmlContent = "<deadlock></deadlock>";
            var doc = XDocument.Parse(xmlContent);

            // Act
            var (processes, resources, victimId) = DeadlockXmlParser.ParseDeadlockXml(doc);

            // Assert
            processes.Should().BeEmpty();
            resources.Should().BeEmpty();
            victimId.Should().BeEmpty();
        }
        
        [Fact]
        public void BuildDeadlockMermaidGraph_ShouldIncludeLogUsedAndWaitResources()
        {
            // Arrange
            string xmlContent = EmbeddedResourceHelper.GetResourceContent("deadlock_bookmark_lookup.xdl");
            var doc = XDocument.Parse(xmlContent);
            var (processes, resources, victimId) = DeadlockXmlParser.ParseDeadlockXml(doc);

            // Act
            var graph = DeadlockGraphBuilder.Build(processes, resources, victimId);
            string mermaid = DeadlockGraphBuilder.GenerateMermaid(graph);

            // Assert
            mermaid.Should().Contain("flowchart TD");
            mermaid.Should().Contain("process1");
            mermaid.Should().Contain("process2");
            mermaid.Should().Contain("100 日志量"); // process1 rollback cost
            mermaid.Should().Contain("5000 日志量"); // process2 rollback cost
            
            // Process1 should be marked as victim
            mermaid.Should().Contain(":::victim");
        }
    }
}
