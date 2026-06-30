using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphNodeUiActionServiceTests
    {
        [Fact]
        public void CreateNodeFromRelOp_MapsCoreNodeFieldsToViewModel()
        {
            var service = new PlanGraphNodeUiActionService();
            XElement relOp = new("RelOp",
                new XAttribute("NodeId", "7"),
                new XAttribute("PhysicalOp", "Index Seek"),
                new XAttribute("LogicalOp", "Index Seek"),
                new XAttribute("EstimateRows", "42"),
                new XAttribute("EstimatedRowsRead", "42"),
                new XAttribute("EstimatedTotalSubtreeCost", "1.5"),
                new XAttribute("EstimateCPU", "0.25"),
                new XAttribute("EstimateIO", "0.75"),
                new XAttribute("AvgRowSize", "128"));

            PlanNodeViewModel node =
                service.CreateNodeFromRelOp(
                    relOp,
                    XNamespace.None,
                    residualIoThreshold: 10,
                    residualIoMinRowsRead: 1000);

            node.RawElement.Should().BeSameAs(relOp);
            node.NodeId.Should().Be("7");
            node.PhysicalOp.Should().Be("Index Seek");
            node.LogicalOp.Should().Be("Index Seek");
            node.SubtreeCost.Should().Be(1.5);
            node.EstRowsNum.Should().Be(42);
            node.EstimatedCPUCostNum.Should().Be(0.25);
            node.EstimatedIOCostNum.Should().Be(0.75);
            node.Location.X.Should().Be(50);
            node.Location.Y.Should().Be(50);
        }

        [Fact]
        public void CreateNodeFromRelOp_AssignsIconVisuals()
        {
            var service = new PlanGraphNodeUiActionService();
            XElement relOp = new("RelOp",
                new XAttribute("NodeId", "1"),
                new XAttribute("PhysicalOp", "Hash Match"),
                new XAttribute("LogicalOp", "Aggregate"));

            PlanNodeViewModel node =
                service.CreateNodeFromRelOp(
                    relOp,
                    XNamespace.None,
                    residualIoThreshold: 10,
                    residualIoMinRowsRead: 1000);

            node.IconGeometry.Should().NotBeNull();
            node.IconBrush.Should().NotBeNull();
        }
    }
}
