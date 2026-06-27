using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphLayoutRefreshServiceTests
    {
        private readonly PlanGraphLayoutRefreshService _service = new();

        [Fact]
        public void Calculate_ReturnsPositionsAndConnectionLayoutDirection()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphLayoutRefreshResult result =
                _service.Calculate(
                    relOps,
                    XNamespace.None,
                    CreateNodes(relOps),
                    PlanGraphLayoutDirection.Horizontal);

            result.ConnectionLayout.Should().Be(PlanGraphLayoutDirection.Horizontal);
            result.NodePositions.Should().HaveCount(3);
            FindByNodeId(result.NodePositions, "0").X.Should().Be(50);
            FindByNodeId(result.NodePositions, "1").X.Should().Be(330);
        }

        [Fact]
        public void Calculate_WhenDirectionIsVertical_ReturnsVerticalConnectionLayout()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphLayoutRefreshResult result =
                _service.Calculate(
                    relOps,
                    XNamespace.None,
                    CreateNodes(relOps),
                    PlanGraphLayoutDirection.Vertical);

            result.ConnectionLayout.Should().Be(PlanGraphLayoutDirection.Vertical);
            FindByNodeId(result.NodePositions, "0").Y.Should().Be(50);
            FindByNodeId(result.NodePositions, "1").Y.Should().Be(210);
        }

        [Fact]
        public void Calculate_WhenRootIsCollapsed_ReturnsOnlyRootPosition()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphLayoutRefreshResult result =
                _service.Calculate(
                    relOps,
                    XNamespace.None,
                    relOps.Select(relOp => new PlanGraphLayoutRefreshNode(
                        relOp,
                        IsCollapsed: NodeId(relOp) == "0")).ToList(),
                    PlanGraphLayoutDirection.Horizontal);

            result.NodePositions.Should().ContainSingle()
                .Which.RelOp.Should().BeSameAs(relOps[0]);
        }

        [Fact]
        public void Calculate_WhenNodeIsMissingFromState_FiltersPositionOut()
        {
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphLayoutRefreshResult result =
                _service.Calculate(
                    relOps,
                    XNamespace.None,
                    relOps
                        .Where(relOp => NodeId(relOp) != "2")
                        .Select(relOp => new PlanGraphLayoutRefreshNode(
                            relOp,
                            IsCollapsed: false))
                        .ToList(),
                    PlanGraphLayoutDirection.Horizontal);

            result.NodePositions.Select(position => NodeId(position.RelOp))
                .Should()
                .BeEquivalentTo(new[] { "0", "1" });
        }

        [Fact]
        public void Calculate_WhenInputsAreEmpty_ReturnsEmptyPositions()
        {
            PlanGraphLayoutRefreshResult result =
                _service.Calculate(
                    new List<XElement>(),
                    XNamespace.None,
                    new List<PlanGraphLayoutRefreshNode>(),
                    PlanGraphLayoutDirection.Horizontal);

            result.NodePositions.Should().BeEmpty();
            result.ConnectionLayout.Should().Be(PlanGraphLayoutDirection.Horizontal);
        }

        private static IReadOnlyList<PlanGraphLayoutRefreshNode> CreateNodes(
            IReadOnlyList<XElement> relOps)
        {
            return relOps
                .Select(relOp => new PlanGraphLayoutRefreshNode(
                    relOp,
                    IsCollapsed: false))
                .ToList();
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

        private static PlanGraphLayoutRefreshPosition FindByNodeId(
            IEnumerable<PlanGraphLayoutRefreshPosition> positions,
            string nodeId)
        {
            return positions.Single(position => NodeId(position.RelOp) == nodeId);
        }

        private static string NodeId(XElement relOp)
        {
            return relOp.Attribute("NodeId")?.Value ?? string.Empty;
        }
    }
}
