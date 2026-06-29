using System.Collections.ObjectModel;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphCollapseUiActionServiceTests
    {
        [Fact]
        public void ApplyCollapseStates_WhenStateExists_UpdatesMatchingNodes()
        {
            var service = new PlanGraphCollapseUiActionService();
            XElement relOp = new("RelOp", new XAttribute("NodeId", "1"));
            var node = new PlanNodeViewModel { RawElement = relOp };

            service.ApplyCollapseStates(
                [node],
                new Dictionary<XElement, bool> { [relOp] = true });

            node.IsCollapsed.Should().BeTrue();
        }

        [Fact]
        public void CalculateExpandAll_ReturnsFalseForEveryRelOp()
        {
            var service = new PlanGraphCollapseUiActionService();
            XElement relOp = new("RelOp", new XAttribute("NodeId", "1"));
            var node = new PlanNodeViewModel
            {
                RawElement = relOp,
                IsCollapsed = true
            };

            IReadOnlyDictionary<XElement, bool> states =
                service.CalculateExpandAll([node]);

            states[relOp].Should().BeFalse();
        }

        [Fact]
        public void CaptureLogSnapshot_UsesOnlyVisibleNodesAndConnections()
        {
            var service = new PlanGraphCollapseUiActionService();
            var visibleSource = new PlanNodeViewModel
            {
                NodeId = "1",
                PhysicalOp = "Index Seek",
                IsVisible = true
            };
            var hiddenTarget = new PlanNodeViewModel
            {
                NodeId = "2",
                PhysicalOp = "Key Lookup",
                IsVisible = false
            };
            var visibleConnection = new ConnectionViewModel
            {
                Source = visibleSource,
                Target = hiddenTarget,
                IsVisible = true
            };
            var hiddenConnection = new ConnectionViewModel
            {
                Source = hiddenTarget,
                Target = visibleSource,
                IsVisible = false
            };

            Core.Services.PlanGraphCollapseLogSnapshot snapshot =
                service.CaptureLogSnapshot(
                    [visibleSource, hiddenTarget],
                    [visibleConnection, hiddenConnection]);

            snapshot.VisibleNodes.Should().ContainSingle()
                .Which.NodeId.Should().Be("1");
            snapshot.VisibleConnections.Should().ContainSingle();
        }

        [Fact]
        public void UpdateVisibility_WhenRootIsCollapsed_HidesChildAndConnection()
        {
            var service = new PlanGraphCollapseUiActionService();
            XNamespace ns = "";
            XElement root = new("RelOp",
                new XAttribute("NodeId", "0"),
                new XElement("RelOp", new XAttribute("NodeId", "1")));
            XDocument document = new(root);
            XElement child = root.Element("RelOp")!;
            var rootNode = new PlanNodeViewModel
            {
                RawElement = root,
                IsCollapsed = true
            };
            var childNode = new PlanNodeViewModel
            {
                RawElement = child
            };
            var connection = new ConnectionViewModel
            {
                Source = childNode,
                Target = rootNode
            };
            var visibleNodes = new ObservableCollection<PlanNodeViewModel>();
            var visibleConnections = new ObservableCollection<ConnectionViewModel>();

            service.UpdateVisibility(
                document,
                ns,
                [rootNode, childNode],
                [connection],
                visibleNodes,
                visibleConnections);

            rootNode.IsVisible.Should().BeTrue();
            childNode.IsVisible.Should().BeFalse();
            connection.IsVisible.Should().BeFalse();
            visibleNodes.Should().BeEquivalentTo([rootNode, childNode]);
            visibleConnections.Should().ContainSingle().Which.Should().BeSameAs(connection);
        }
    }
}
