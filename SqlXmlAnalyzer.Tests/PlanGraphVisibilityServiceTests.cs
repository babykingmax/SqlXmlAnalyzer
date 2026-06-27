using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphVisibilityServiceTests
    {
        [Fact]
        public void CalculateVisibility_WhenNothingIsCollapsed_ShowsAllNodesAndConnections()
        {
            var service = new PlanGraphVisibilityService();
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphVisibilityResult result =
                service.CalculateVisibility(
                    relOps,
                    XNamespace.None,
                    collapsedRelOps: null);

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
        public void CalculateVisibility_WhenRootIsCollapsed_ShowsOnlyRoot()
        {
            var service = new PlanGraphVisibilityService();
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphVisibilityResult result =
                service.CalculateVisibility(
                    relOps,
                    XNamespace.None,
                    new HashSet<XElement> { relOps[0] });

            result.VisibleRelOps.Should().ContainSingle()
                .Which.Should().BeSameAs(relOps[0]);
            result.VisibleConnections.Should().BeEmpty();
        }

        [Fact]
        public void CalculateVisibility_WhenChildIsCollapsed_HidesGrandchildren()
        {
            var service = new PlanGraphVisibilityService();
            XDocument document = CreateNestedPlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            XElement child = relOps.Single(relOp => NodeId(relOp) == "1");

            PlanGraphVisibilityResult result =
                service.CalculateVisibility(
                    relOps,
                    XNamespace.None,
                    new HashSet<XElement> { child });

            result.VisibleRelOps.Select(NodeId)
                .Should()
                .BeEquivalentTo(new[] { "0", "1" });
            result.VisibleConnections.Should().ContainSingle(connection =>
                NodeId(connection.SourceRelOp) == "1"
                && NodeId(connection.TargetRelOp) == "0");
        }

        [Fact]
        public void CalculateVisibility_WhenRelOpsAreEmpty_ReturnsEmptyResult()
        {
            var service = new PlanGraphVisibilityService();

            PlanGraphVisibilityResult result =
                service.CalculateVisibility(
                    new List<XElement>(),
                    XNamespace.None,
                    collapsedRelOps: null);

            result.VisibleRelOps.Should().BeEmpty();
            result.VisibleConnections.Should().BeEmpty();
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

        private static XDocument CreateNestedPlan()
        {
            return new XDocument(
                new XElement("ShowPlan",
                    new XElement("RelOp",
                        new XAttribute("NodeId", "0"),
                        new XElement("NestedLoops",
                            new XElement("RelOp",
                                new XAttribute("NodeId", "1"),
                                new XElement("IndexScan",
                                    new XElement("RelOp",
                                        new XAttribute("NodeId", "2"))))))));
        }

        private static string NodeId(XElement relOp)
        {
            return relOp.Attribute("NodeId")?.Value ?? string.Empty;
        }
    }
}
