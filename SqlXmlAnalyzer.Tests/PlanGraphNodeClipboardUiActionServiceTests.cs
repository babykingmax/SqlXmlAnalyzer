using FluentAssertions;
using SqlXmlAnalyzer.Services;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphNodeClipboardUiActionServiceTests
    {
        [Theory]
        [InlineData(null, "N/A")]
        [InlineData("invalid", "N/A")]
        [InlineData("NaN", "N/A")]
        [InlineData("-1", "N/A")]
        [InlineData("0", "0 (0.0%)")]
        [InlineData("3.25", "3.25 (0.0%)")]
        public void BuildNodeInfo_PreservesCostAvailabilityFromLoadedOperator(string? rawCost, string expected)
        {
            var relOp = new XElement("RelOp", new XAttribute("NodeId", "0"),
                new XAttribute("PhysicalOp", "Constant Scan"), new XAttribute("EstimateRows", "1"));
            if (rawCost != null) relOp.SetAttributeValue("EstimatedTotalSubtreeCost", rawCost);
            var node = new PlanGraphNodeUiActionService().CreateNodeFromRelOp(relOp, XNamespace.None, 10, 1000);
            node.CostPercent = 0;

            string text = new PlanGraphNodeClipboardUiActionService().BuildNodeInfo(node);

            text.Should().Contain("Estimated Cost: " + expected + Environment.NewLine);
            if (expected == "N/A") node.EstimatedSubtreeCostStr.Should().Be("N/A");
        }

        [Fact]
        public void BuildNodeInfo_IncludesCoreNodeFields()
        {
            var service = new PlanGraphNodeClipboardUiActionService();
            var node = new PlanNodeViewModel
            {
                NodeId = "11",
                PhysicalOp = "Index Seek",
                LogicalOp = "Index Seek",
                SubtreeCost = 3.25,
                CostPercent = 42,
                EstRows = "100",
                ActualRows = "95",
                EstimatedDataSize = "24 KB"
            };

            string text = service.BuildNodeInfo(node);

            text.Should().Contain("Node ID: 11");
            text.Should().Contain("Physical Op: Index Seek");
            text.Should().Contain("Logical Op: Index Seek");
            text.Should().Contain("Estimated Rows: 100");
            text.Should().Contain("Actual Rows: 95");
            text.Should().Contain("Estimated Data Size: 24 KB");
        }

        [Fact]
        public void BuildNodeInfo_IncludesOptionalDetailsWhenPresent()
        {
            var service = new PlanGraphNodeClipboardUiActionService();
            var node = new PlanNodeViewModel
            {
                NodeId = "12",
                PhysicalOp = "Filter",
                LogicalOp = "Filter",
                ObjectDetails = "[dbo].[Orders]",
                OutputList = "[OrderId]",
                SeekPredicates = "[OrderId]=(1)",
                Predicate = "[Status]='Open'",
                Warnings = "Residual predicate"
            };

            string text = service.BuildNodeInfo(node);

            text.Should().Contain("Object: [dbo].[Orders]");
            text.Should().Contain("Output List: [OrderId]");
            text.Should().Contain("Seek Predicates: [OrderId]=(1)");
            text.Should().Contain("Predicate: [Status]='Open'");
            text.Should().Contain("Warnings: Residual predicate");
        }
    }
}
