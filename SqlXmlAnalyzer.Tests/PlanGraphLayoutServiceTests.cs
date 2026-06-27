using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphLayoutServiceTests
    {
        [Fact]
        public void CalculateLayout_WhenHorizontal_PlacesRootAndChildrenLikePlanGraphControl()
        {
            var service = new PlanGraphLayoutService();
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            IReadOnlyList<PlanGraphLayoutPosition> positions =
                service.CalculateLayout(
                    relOps,
                    XNamespace.None,
                    collapsedRelOps: null,
                    PlanGraphLayoutDirection.Horizontal);

            PlanGraphLayoutPosition root = FindByNodeId(positions, "0");
            PlanGraphLayoutPosition childOne = FindByNodeId(positions, "1");
            PlanGraphLayoutPosition childTwo = FindByNodeId(positions, "2");

            root.SubtreeWidth.Should().Be(2);
            root.X.Should().Be(50);
            root.Y.Should().Be(130);
            childOne.SubtreeWidth.Should().Be(1);
            childOne.X.Should().Be(330);
            childOne.Y.Should().Be(50);
            childTwo.X.Should().Be(330);
            childTwo.Y.Should().Be(210);
        }

        [Fact]
        public void CalculateLayout_WhenVertical_PlacesRootAndChildrenLikePlanGraphControl()
        {
            var service = new PlanGraphLayoutService();
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            IReadOnlyList<PlanGraphLayoutPosition> positions =
                service.CalculateLayout(
                    relOps,
                    XNamespace.None,
                    collapsedRelOps: null,
                    PlanGraphLayoutDirection.Vertical);

            PlanGraphLayoutPosition root = FindByNodeId(positions, "0");
            PlanGraphLayoutPosition childOne = FindByNodeId(positions, "1");
            PlanGraphLayoutPosition childTwo = FindByNodeId(positions, "2");

            root.SubtreeWidth.Should().Be(2);
            root.X.Should().Be(190);
            root.Y.Should().Be(50);
            childOne.X.Should().Be(50);
            childOne.Y.Should().Be(210);
            childTwo.X.Should().Be(330);
            childTwo.Y.Should().Be(210);
        }

        [Fact]
        public void CalculateLayout_WhenRootIsCollapsed_ReturnsOnlyRootPosition()
        {
            var service = new PlanGraphLayoutService();
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            var collapsed = new HashSet<XElement> { relOps[0] };

            IReadOnlyList<PlanGraphLayoutPosition> positions =
                service.CalculateLayout(
                    relOps,
                    XNamespace.None,
                    collapsed,
                    PlanGraphLayoutDirection.Horizontal);

            positions.Should().ContainSingle();
            positions[0].Element.Should().BeSameAs(relOps[0]);
            positions[0].SubtreeWidth.Should().Be(1);
            positions[0].X.Should().Be(50);
            positions[0].Y.Should().Be(50);
        }

        [Fact]
        public void CalculateLayout_WhenRelOpsAreEmpty_ReturnsEmpty()
        {
            var service = new PlanGraphLayoutService();

            IReadOnlyList<PlanGraphLayoutPosition> positions =
                service.CalculateLayout(
                    new List<XElement>(),
                    XNamespace.None,
                    collapsedRelOps: null,
                    PlanGraphLayoutDirection.Horizontal);

            positions.Should().BeEmpty();
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

        private static PlanGraphLayoutPosition FindByNodeId(
            IEnumerable<PlanGraphLayoutPosition> positions,
            string nodeId)
        {
            return positions.Single(position =>
                position.Element.Attribute("NodeId")?.Value == nodeId);
        }
    }
}
