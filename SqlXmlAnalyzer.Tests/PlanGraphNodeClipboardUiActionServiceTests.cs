using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphNodeClipboardUiActionServiceTests
    {
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
