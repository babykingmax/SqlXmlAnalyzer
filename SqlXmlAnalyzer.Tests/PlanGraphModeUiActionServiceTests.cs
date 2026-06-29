using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphModeUiActionServiceTests
    {
        [Fact]
        public void ApplyViewMode_WhenSelectedIndexIsValid_UpdatesAllNodes()
        {
            var service = new PlanGraphModeUiActionService();
            PlanNodeViewModel[] nodes =
            [
                new PlanNodeViewModel(),
                new PlanNodeViewModel()
            ];

            service.ApplyViewMode((int)DiagramViewMode.Rows, nodes);

            nodes.Should().AllSatisfy(node =>
                node.ViewMode.Should().Be(DiagramViewMode.Rows));
        }

        [Fact]
        public void ApplyColorMode_WhenSelectedIndexIsValid_UpdatesAllNodes()
        {
            var service = new PlanGraphModeUiActionService();
            PlanNodeViewModel[] nodes =
            [
                new PlanNodeViewModel(),
                new PlanNodeViewModel()
            ];

            service.ApplyColorMode((int)PlanColorMode.IoCost, mode =>
            {
                service.ApplyColorMode(mode, nodes);
            });

            nodes.Should().AllSatisfy(node =>
                node.ColorMode.Should().Be(PlanColorMode.IoCost));
        }

        [Fact]
        public void ApplyLinkMetric_WhenSelectedIndexIsValid_UpdatesAllConnections()
        {
            var service = new PlanGraphModeUiActionService();
            ConnectionViewModel[] connections =
            [
                new ConnectionViewModel(),
                new ConnectionViewModel()
            ];

            service.ApplyLinkMetric((int)LinkMetricMode.DataSize, metric =>
            {
                service.ApplyLinkMetric(metric, connections);
            });

            connections.Should().AllSatisfy(connection =>
                connection.CurrentLinkMetric.Should().Be(LinkMetricMode.DataSize));
        }

        [Fact]
        public void ApplyLayoutMode_WhenSelectedIndexIsValid_InvokesSetter()
        {
            var service = new PlanGraphModeUiActionService();
            PlanLayoutMode layoutMode = PlanLayoutMode.Horizontal;

            service.ApplyLayoutMode(
                (int)PlanLayoutMode.Vertical,
                mode => layoutMode = mode);

            layoutMode.Should().Be(PlanLayoutMode.Vertical);
        }

        [Fact]
        public void ApplyModes_WhenSelectedIndexIsNegative_DoesNotChangeState()
        {
            var service = new PlanGraphModeUiActionService();
            var node = new PlanNodeViewModel();
            bool layoutSetterCalled = false;
            bool colorSetterCalled = false;
            bool metricSetterCalled = false;

            service.ApplyViewMode(-1, [node]);
            service.ApplyLayoutMode(-1, _ => layoutSetterCalled = true);
            service.ApplyColorMode(-1, _ => colorSetterCalled = true);
            service.ApplyLinkMetric(-1, _ => metricSetterCalled = true);

            node.ViewMode.Should().Be(DiagramViewMode.CostPercent);
            layoutSetterCalled.Should().BeFalse();
            colorSetterCalled.Should().BeFalse();
            metricSetterCalled.Should().BeFalse();
        }
    }
}
