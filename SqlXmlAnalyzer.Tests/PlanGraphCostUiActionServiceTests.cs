using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphCostUiActionServiceTests
    {
        [Fact]
        public void ApplyCostCalculations_WhenNodeHasChildren_SetsChildrenAndDisplayModes()
        {
            var service = new PlanGraphCostUiActionService();
            XDocument document = CreateTwoNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            XElement rootRelOp = relOps[0];
            XElement childRelOp = relOps[1];
            var root = new PlanNodeViewModel
            {
                RawElement = rootRelOp,
                SubtreeCost = 10,
                EstimatedCPUCostNum = 6,
                EstimatedIOCostNum = 4,
                EstRowsNum = 100
            };
            var child = new PlanNodeViewModel
            {
                RawElement = childRelOp,
                SubtreeCost = 3,
                EstimatedCPUCostNum = 2,
                EstimatedIOCostNum = 1,
                EstRowsNum = 25
            };

            service.ApplyCostCalculations(
                relOps,
                new Dictionary<XElement, PlanNodeViewModel>
                {
                    [rootRelOp] = root,
                    [childRelOp] = child
                },
                XNamespace.None,
                DiagramViewMode.Rows,
                PlanColorMode.CpuCost);

            root.HasChildren.Should().BeTrue();
            child.HasChildren.Should().BeFalse();
            root.OwnCost.Should().BeGreaterThan(0);
            root.CostPercent.Should().BeGreaterThan(0);
            root.ViewMode.Should().Be(DiagramViewMode.Rows);
            child.ColorMode.Should().Be(PlanColorMode.CpuCost);
        }

        [Fact]
        public void ApplyCostCalculations_WhenChildNodeIsNotInMap_UsesChildAttributeCost()
        {
            var service = new PlanGraphCostUiActionService();
            XDocument document = CreateTwoNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            XElement rootRelOp = relOps[0];
            var root = new PlanNodeViewModel
            {
                RawElement = rootRelOp,
                SubtreeCost = 10,
                EstimatedCPUCostNum = 6,
                EstimatedIOCostNum = 4,
                EstRowsNum = 100
            };

            service.ApplyCostCalculations(
                [rootRelOp],
                new Dictionary<XElement, PlanNodeViewModel>
                {
                    [rootRelOp] = root
                },
                XNamespace.None,
                DiagramViewMode.CostPercent,
                PlanColorMode.TotalCost);

            root.HasChildren.Should().BeTrue();
            root.OwnCost.Should().BeGreaterThan(0);
        }

        private static XDocument CreateTwoNodePlan()
        {
            return new XDocument(
                new XElement("ShowPlan",
                    new XElement("RelOp",
                        new XAttribute("NodeId", "0"),
                        new XAttribute("EstimatedTotalSubtreeCost", "10"),
                        new XElement("RelOp",
                            new XAttribute("NodeId", "1"),
                            new XAttribute("EstimatedTotalSubtreeCost", "3")))));
        }
    }
}
