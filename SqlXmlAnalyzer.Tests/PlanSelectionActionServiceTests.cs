using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanSelectionActionServiceTests
    {
        [Fact]
        public void SelectFromOperatorTreeItem_WhenTagIsRelOp_ReturnsSelection()
        {
            var service = new PlanSelectionActionService();
            XElement relOp = CreateRelOp();
            var item = new FakeTreeItem { Tag = relOp };

            PlanSelectionResult result = service.SelectFromOperatorTreeItem(item);

            result.HasSelection.Should().BeTrue();
            result.Source.Should().Be(PlanSelectionSource.OperatorTree);
            result.RelOp.Should().BeSameAs(relOp);
        }

        [Fact]
        public void SelectFromOperatorTreeItem_WhenValueIsNotTreeItem_ReturnsMissing()
        {
            var service = new PlanSelectionActionService();

            PlanSelectionResult result = service.SelectFromOperatorTreeItem(new object());

            result.HasSelection.Should().BeFalse();
            result.Source.Should().Be(PlanSelectionSource.Missing);
            result.RelOp.Should().BeNull();
        }

        [Fact]
        public void SelectFromVisualTreeNode_WhenTagIsRelOp_ReturnsSelection()
        {
            var service = new PlanSelectionActionService();
            XElement relOp = CreateRelOp();
            var node = new PlanVisualNode { Tag = relOp };

            PlanSelectionResult result = service.SelectFromVisualTreeNode(node);

            result.HasSelection.Should().BeTrue();
            result.Source.Should().Be(PlanSelectionSource.VisualTree);
            result.RelOp.Should().BeSameAs(relOp);
        }

        [Fact]
        public void SelectFromVisualTreeNode_WhenTagIsMissing_ReturnsMissing()
        {
            var service = new PlanSelectionActionService();

            PlanSelectionResult result =
                service.SelectFromVisualTreeNode(new PlanVisualNode());

            result.HasSelection.Should().BeFalse();
            result.Source.Should().Be(PlanSelectionSource.Missing);
            result.RelOp.Should().BeNull();
        }

        [Fact]
        public void SelectFromGraphNode_WhenRawElementIsRelOp_ReturnsSelection()
        {
            var service = new PlanSelectionActionService();
            XElement relOp = CreateRelOp();
            var node = new FakeGraphNode { RawElement = relOp };

            PlanSelectionResult result = service.SelectFromGraphNode(node);

            result.HasSelection.Should().BeTrue();
            result.Source.Should().Be(PlanSelectionSource.GraphNode);
            result.RelOp.Should().BeSameAs(relOp);
        }

        [Fact]
        public void SelectFromGraphNode_WhenValueIsNull_ReturnsMissing()
        {
            var service = new PlanSelectionActionService();

            PlanSelectionResult result = service.SelectFromGraphNode(null);

            result.HasSelection.Should().BeFalse();
            result.Source.Should().Be(PlanSelectionSource.Missing);
            result.RelOp.Should().BeNull();
        }

        private static XElement CreateRelOp()
        {
            return new XElement("RelOp",
                new XAttribute("PhysicalOp", "Index Seek"));
        }

        private sealed class FakeGraphNode
        {
            public XElement? RawElement { get; init; }
        }

        private sealed class FakeTreeItem
        {
            public XElement? Tag { get; init; }
        }
    }
}
