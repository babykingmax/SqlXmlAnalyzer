using System.Collections.ObjectModel;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphLoadUiActionServiceTests
    {
        [Fact]
        public void Load_WhenDocumentIsMissing_ReturnsEmptyResultAndClearsCollections()
        {
            var service = new PlanGraphLoadUiActionService();
            var nodes = new ObservableCollection<PlanNodeViewModel>
            {
                new PlanNodeViewModel()
            };
            var connections = new ObservableCollection<ConnectionViewModel>
            {
                new ConnectionViewModel()
            };

            PlanGraphLoadUiActionResult result =
                service.Load(
                    null,
                    XNamespace.None,
                    nodes,
                    connections,
                    CreateOptions());

            result.HasGraph.Should().BeFalse();
            result.MasterNodes.Should().BeEmpty();
            result.MasterConnections.Should().BeEmpty();
            result.SelectedNode.Should().BeNull();
            nodes.Should().BeEmpty();
            connections.Should().BeEmpty();
        }

        [Fact]
        public void Load_WhenPlanHasRelOps_BuildsNodesConnectionsAndSelection()
        {
            var service = new PlanGraphLoadUiActionService();
            XDocument document = CreateTwoNodePlan();
            var nodes = new ObservableCollection<PlanNodeViewModel>();
            var connections = new ObservableCollection<ConnectionViewModel>();

            PlanGraphLoadUiActionResult result =
                service.Load(
                    document,
                    XNamespace.None,
                    nodes,
                    connections,
                    CreateOptions());

            result.HasGraph.Should().BeTrue();
            result.MasterNodes.Should().HaveCount(2);
            result.MasterConnections.Should().ContainSingle();
            result.SelectedNode.Should().NotBeNull();
            nodes.Should().HaveCount(2);
            connections.Should().ContainSingle();
            connections[0].Source.Should().BeSameAs(nodes.Single(node => node.NodeId == "1"));
            connections[0].Target.Should().BeSameAs(nodes.Single(node => node.NodeId == "0"));
        }

        [Fact]
        public void Load_AppliesInitialModesToNodesAndConnections()
        {
            var service = new PlanGraphLoadUiActionService();
            XDocument document = CreateTwoNodePlan();
            var nodes = new ObservableCollection<PlanNodeViewModel>();
            var connections = new ObservableCollection<ConnectionViewModel>();
            PlanGraphLoadUiActionOptions options = CreateOptions() with
            {
                InitialLayout = PlanLayoutMode.Vertical,
                InitialColor = PlanColorMode.IoCost,
                InitialView = DiagramViewMode.Rows,
                InitialLinkMetric = LinkMetricMode.DataSize
            };

            service.Load(
                document,
                XNamespace.None,
                nodes,
                connections,
                options);

            nodes.Should().AllSatisfy(node =>
            {
                node.ViewMode.Should().Be(DiagramViewMode.Rows);
                node.ColorMode.Should().Be(PlanColorMode.IoCost);
            });
            connections.Should().AllSatisfy(connection =>
            {
                connection.LayoutMode.Should().Be(PlanLayoutMode.Vertical);
                connection.CurrentLinkMetric.Should().Be(LinkMetricMode.DataSize);
            });
        }

        private static PlanGraphLoadUiActionOptions CreateOptions()
        {
            return new PlanGraphLoadUiActionOptions
            {
                InitialLayout = PlanLayoutMode.Horizontal,
                InitialColor = PlanColorMode.TotalCost,
                InitialView = DiagramViewMode.CostPercent,
                InitialLinkMetric = LinkMetricMode.RowCount,
                ResidualIoThreshold = 10,
                ResidualIoMinRowsRead = 1000
            };
        }

        private static XDocument CreateTwoNodePlan()
        {
            return new XDocument(
                new XElement("ShowPlan",
                    new XElement("RelOp",
                        new XAttribute("NodeId", "0"),
                        new XAttribute("PhysicalOp", "Select"),
                        new XAttribute("LogicalOp", "Select"),
                        new XAttribute("EstimatedTotalSubtreeCost", "10"),
                        new XAttribute("EstimateRows", "1"),
                        new XElement("RelOp",
                            new XAttribute("NodeId", "1"),
                            new XAttribute("PhysicalOp", "Index Seek"),
                            new XAttribute("LogicalOp", "Index Seek"),
                            new XAttribute("EstimatedTotalSubtreeCost", "4"),
                            new XAttribute("EstimateRows", "10")))));
        }
    }
}
