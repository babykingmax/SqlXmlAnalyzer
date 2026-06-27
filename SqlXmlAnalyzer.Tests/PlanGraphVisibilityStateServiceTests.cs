using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphVisibilityStateServiceTests
    {
        private readonly PlanGraphVisibilityStateService _service = new();

        [Fact]
        public void Calculate_WhenNothingIsCollapsed_ReturnsVisibleNodesAndMasterConnections()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            IReadOnlyList<PlanGraphVisibilityStateNode> nodes = relOps
                .Select(relOp => new PlanGraphVisibilityStateNode(relOp, IsCollapsed: false))
                .ToList();
            IReadOnlyList<PlanGraphVisibilityStateConnection> connections =
                CreateMasterConnections(relOps);

            PlanGraphVisibilityStateResult result =
                _service.Calculate(relOps, XNamespace.None, nodes, connections);

            result.VisibleRelOps.Should().BeEquivalentTo(relOps);
            result.VisibleConnections.Should().HaveCount(2);
            result.VisibleConnections.Should().Contain(connection =>
                NodeId(connection.SourceRelOp) == "1"
                && NodeId(connection.TargetRelOp) == "0");
            result.VisibleConnections.Should().Contain(connection =>
                NodeId(connection.SourceRelOp) == "2"
                && NodeId(connection.TargetRelOp) == "0");
        }

        [Fact]
        public void Calculate_WhenRootIsCollapsed_ReturnsOnlyRootAndNoConnections()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            IReadOnlyList<PlanGraphVisibilityStateNode> nodes = relOps
                .Select(relOp => new PlanGraphVisibilityStateNode(
                    relOp,
                    IsCollapsed: NodeId(relOp) == "0"))
                .ToList();

            PlanGraphVisibilityStateResult result =
                _service.Calculate(
                    relOps,
                    XNamespace.None,
                    nodes,
                    CreateMasterConnections(relOps));

            result.VisibleRelOps.Should().ContainSingle()
                .Which.Should().BeSameAs(relOps[0]);
            result.VisibleConnections.Should().BeEmpty();
        }

        [Fact]
        public void Calculate_WhenConnectionIsNotInMasterList_FiltersItOut()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            IReadOnlyList<PlanGraphVisibilityStateNode> nodes = relOps
                .Select(relOp => new PlanGraphVisibilityStateNode(relOp, IsCollapsed: false))
                .ToList();
            var masterConnections = new[]
            {
                new PlanGraphVisibilityStateConnection(relOps[1], relOps[0])
            };

            PlanGraphVisibilityStateResult result =
                _service.Calculate(relOps, XNamespace.None, nodes, masterConnections);

            result.VisibleConnections.Should().ContainSingle()
                .Which.Should().Be(masterConnections[0]);
        }

        [Fact]
        public void Calculate_WhenNodeIsNotInState_FiltersItOut()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            IReadOnlyList<PlanGraphVisibilityStateNode> nodes = relOps
                .Where(relOp => NodeId(relOp) != "2")
                .Select(relOp => new PlanGraphVisibilityStateNode(relOp, IsCollapsed: false))
                .ToList();

            PlanGraphVisibilityStateResult result =
                _service.Calculate(
                    relOps,
                    XNamespace.None,
                    nodes,
                    CreateMasterConnections(relOps));

            result.VisibleRelOps.Select(NodeId)
                .Should()
                .BeEquivalentTo(new[] { "0", "1" });
        }

        [Fact]
        public void Calculate_WhenInputsAreEmpty_ReturnsEmptyResult()
        {
            PlanGraphVisibilityStateResult result =
                _service.Calculate(
                    new List<XElement>(),
                    XNamespace.None,
                    new List<PlanGraphVisibilityStateNode>(),
                    new List<PlanGraphVisibilityStateConnection>());

            result.VisibleRelOps.Should().BeEmpty();
            result.VisibleConnections.Should().BeEmpty();
        }

        private static IReadOnlyList<PlanGraphVisibilityStateConnection> CreateMasterConnections(
            IReadOnlyList<XElement> relOps)
        {
            return new[]
            {
                new PlanGraphVisibilityStateConnection(relOps[1], relOps[0]),
                new PlanGraphVisibilityStateConnection(relOps[2], relOps[0])
            };
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
