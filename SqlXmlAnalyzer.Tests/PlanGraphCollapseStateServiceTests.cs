using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphCollapseStateServiceTests
    {
        private readonly PlanGraphCollapseStateService _service = new();

        [Fact]
        public void CalculateExpandAll_ReturnsFalseForEveryNode()
        {
            XDocument document = CreatePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphCollapseStateResult result =
                _service.CalculateExpandAll(new[]
                {
                    CreateNode(relOps[0], isCollapsed: true),
                    CreateNode(relOps[1], isCollapsed: true),
                    CreateNode(relOps[2], isCollapsed: false)
                });

            result.CollapsedStates.Values.Should().OnlyContain(isCollapsed => !isCollapsed);
        }

        [Fact]
        public void CalculateSmartCollapse_CollapsesLowCostCleanBranchesAndExpandsOthers()
        {
            XDocument document = CreatePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphCollapseStateResult result =
                _service.CalculateSmartCollapse(new[]
                {
                    CreateNode(relOps[0], hasChildren: true, subtreeCost: 100, isCollapsed: true),
                    CreateNode(relOps[1], hasChildren: true, subtreeCost: 4, isCollapsed: false),
                    CreateNode(relOps[2], hasChildren: false, subtreeCost: 1, isCollapsed: true),
                    CreateNode(relOps[3], hasChildren: false, subtreeCost: 80, isCollapsed: true)
                });

            result.CollapsedStates[relOps[0]].Should().BeFalse();
            result.CollapsedStates[relOps[1]].Should().BeTrue();
            result.CollapsedStates[relOps[2]].Should().BeFalse();
            result.CollapsedStates[relOps[3]].Should().BeFalse();
        }

        [Fact]
        public void CalculateSmartCollapse_WhenLowCostBranchContainsWarning_DoesNotCollapseWarningSubtree()
        {
            XDocument document = CreatePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphCollapseStateResult result =
                _service.CalculateSmartCollapse(new[]
                {
                    CreateNode(relOps[0], hasChildren: true, subtreeCost: 100),
                    CreateNode(relOps[1], hasChildren: true, subtreeCost: 4),
                    CreateNode(relOps[2], hasChildren: false, subtreeCost: 1, severity: "Warning"),
                    CreateNode(relOps[3], hasChildren: false, subtreeCost: 80)
                });

            result.CollapsedStates.Values.Should().OnlyContain(isCollapsed => !isCollapsed);
        }

        [Fact]
        public void CalculateToggle_TogglesOnlyTargetAndPreservesOtherStates()
        {
            XDocument document = CreatePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();

            PlanGraphCollapseStateResult result =
                _service.CalculateToggle(
                    new[]
                    {
                        CreateNode(relOps[0], isCollapsed: false),
                        CreateNode(relOps[1], isCollapsed: true),
                        CreateNode(relOps[2], isCollapsed: false)
                    },
                    relOps[0]);

            result.CollapsedStates[relOps[0]].Should().BeTrue();
            result.CollapsedStates[relOps[1]].Should().BeTrue();
            result.CollapsedStates[relOps[2]].Should().BeFalse();
        }

        [Fact]
        public void CalculateToggle_WhenTargetIsNotPresent_PreservesAllStates()
        {
            XDocument document = CreatePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            XElement missingRelOp = new("RelOp");

            PlanGraphCollapseStateResult result =
                _service.CalculateToggle(
                    new[]
                    {
                        CreateNode(relOps[0], isCollapsed: false),
                        CreateNode(relOps[1], isCollapsed: true)
                    },
                    missingRelOp);

            result.CollapsedStates[relOps[0]].Should().BeFalse();
            result.CollapsedStates[relOps[1]].Should().BeTrue();
        }

        [Fact]
        public void CalculateExpandAll_WhenNodesAreEmpty_ReturnsEmpty()
        {
            PlanGraphCollapseStateResult result =
                _service.CalculateExpandAll(
                    new List<PlanGraphCollapseStateNode>());

            result.CollapsedStates.Should().BeEmpty();
        }

        private static PlanGraphCollapseStateNode CreateNode(
            XElement relOp,
            bool hasChildren = false,
            double subtreeCost = 1,
            string severity = "Info",
            bool isCollapsed = false)
        {
            return new PlanGraphCollapseStateNode(
                relOp,
                hasChildren,
                subtreeCost,
                severity,
                isCollapsed);
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
