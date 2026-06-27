using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphConnectionBuilderServiceTests
    {
        private readonly PlanGraphConnectionBuilderService _service = new();

        [Fact]
        public void BuildConnections_WhenPlanHasDirectChildren_ReturnsChildToParentPairs()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            IReadOnlyList<PlanGraphConnectionPair> result =
                _service.BuildConnections(relOps, XNamespace.None);

            result.Should().HaveCount(2);
            result.Should().Contain(connection =>
                NodeId(connection.SourceRelOp) == "1"
                && NodeId(connection.TargetRelOp) == "0");
            result.Should().Contain(connection =>
                NodeId(connection.SourceRelOp) == "2"
                && NodeId(connection.TargetRelOp) == "0");
        }

        [Fact]
        public void BuildConnections_WhenChildIsOutsideRelOpSet_SkipsConnection()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp")
                .Where(relOp => NodeId(relOp) != "2")
                .ToList();

            IReadOnlyList<PlanGraphConnectionPair> result =
                _service.BuildConnections(relOps, XNamespace.None);

            result.Should().ContainSingle(connection =>
                NodeId(connection.SourceRelOp) == "1"
                && NodeId(connection.TargetRelOp) == "0");
        }

        [Fact]
        public void BuildConnections_WhenRelOpsAreEmpty_ReturnsEmpty()
        {
            IReadOnlyList<PlanGraphConnectionPair> result =
                _service.BuildConnections(new List<XElement>(), XNamespace.None);

            result.Should().BeEmpty();
        }

        private static XDocument CreateThreeNodePlan()
        {
            return new XDocument(
                new XElement("ShowPlan",
                    new XElement("RelOp",
                        new XAttribute("NodeId", "0"),
                        new XElement("NestedLoops",
                            new XElement("RelOp", new XAttribute("NodeId", "1")),
                            new XElement("RelOp", new XAttribute("NodeId", "2"))))));
        }

        private static string NodeId(XElement relOp)
        {
            return relOp.Attribute("NodeId")?.Value ?? string.Empty;
        }
    }
}
