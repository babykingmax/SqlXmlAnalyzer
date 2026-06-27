using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphSmartCollapseServiceTests
    {
        [Fact]
        public void CalculateCollapsedRelOps_WhenChildBranchIsLowCostAndClean_CollapsesBranchRoot()
        {
            var service = new PlanGraphSmartCollapseService();
            XDocument document = CreatePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphSmartCollapseResult result =
                service.CalculateCollapsedRelOps(new[]
                {
                    CreateNode(relOps[0], hasChildren: true, subtreeCost: 100, severity: "Info"),
                    CreateNode(relOps[1], hasChildren: true, subtreeCost: 4, severity: "Info"),
                    CreateNode(relOps[2], hasChildren: false, subtreeCost: 1, severity: "Info"),
                    CreateNode(relOps[3], hasChildren: false, subtreeCost: 80, severity: "Info")
                });

            result.CollapsedRelOps.Should().ContainSingle()
                .Which.Should().BeSameAs(relOps[1]);
        }

        [Fact]
        public void CalculateCollapsedRelOps_WhenLowCostBranchContainsWarning_DoesNotCollapseWarningSubtree()
        {
            var service = new PlanGraphSmartCollapseService();
            XDocument document = CreatePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphSmartCollapseResult result =
                service.CalculateCollapsedRelOps(new[]
                {
                    CreateNode(relOps[0], hasChildren: true, subtreeCost: 100, severity: "Info"),
                    CreateNode(relOps[1], hasChildren: true, subtreeCost: 4, severity: "Info"),
                    CreateNode(relOps[2], hasChildren: false, subtreeCost: 1, severity: "Warning"),
                    CreateNode(relOps[3], hasChildren: false, subtreeCost: 80, severity: "Info")
                });

            result.CollapsedRelOps.Should().BeEmpty();
        }

        [Fact]
        public void CalculateCollapsedRelOps_WhenAllCostsAreZero_UsesFallbackMaxCost()
        {
            var service = new PlanGraphSmartCollapseService();
            XDocument document = CreatePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphSmartCollapseResult result =
                service.CalculateCollapsedRelOps(new[]
                {
                    CreateNode(relOps[0], hasChildren: true, subtreeCost: 0, severity: "Info"),
                    CreateNode(relOps[1], hasChildren: true, subtreeCost: 0, severity: "Info")
                });

            result.CollapsedRelOps.Should().BeEquivalentTo(new[] { relOps[0], relOps[1] });
        }

        [Fact]
        public void CalculateCollapsedRelOps_WhenNoNodes_ReturnsEmpty()
        {
            var service = new PlanGraphSmartCollapseService();

            PlanGraphSmartCollapseResult result =
                service.CalculateCollapsedRelOps(new List<PlanGraphSmartCollapseNode>());

            result.CollapsedRelOps.Should().BeEmpty();
        }

        private static PlanGraphSmartCollapseNode CreateNode(
            XElement relOp,
            bool hasChildren,
            double subtreeCost,
            string severity)
        {
            return new PlanGraphSmartCollapseNode(
                relOp,
                hasChildren,
                subtreeCost,
                severity);
        }

        private static XDocument CreatePlan()
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
                                        new XAttribute("NodeId", "2")))),
                            new XElement("RelOp", new XAttribute("NodeId", "3"))))));
        }
    }
}
