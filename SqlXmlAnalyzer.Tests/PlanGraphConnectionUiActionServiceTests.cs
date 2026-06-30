using System.Collections.ObjectModel;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphConnectionUiActionServiceTests
    {
        [Fact]
        public void BuildConnections_WhenRelOpsHaveChildren_AddsConnectionViewModels()
        {
            var service = new PlanGraphConnectionUiActionService();
            XDocument document = CreateTwoNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            PlanNodeViewModel root = new() { RawElement = relOps[0], NodeId = "0" };
            PlanNodeViewModel child = new() { RawElement = relOps[1], NodeId = "1" };
            var connections = new ObservableCollection<ConnectionViewModel>();

            service.BuildConnections(
                relOps,
                XNamespace.None,
                new Dictionary<XElement, PlanNodeViewModel>
                {
                    [relOps[0]] = root,
                    [relOps[1]] = child
                },
                connections,
                PlanLayoutMode.Vertical,
                LinkMetricMode.DataSize);

            ConnectionViewModel connection =
                connections.Should().ContainSingle().Subject;
            connection.Source.Should().BeSameAs(child);
            connection.Target.Should().BeSameAs(root);
            connection.LayoutMode.Should().Be(PlanLayoutMode.Vertical);
            connection.CurrentLinkMetric.Should().Be(LinkMetricMode.DataSize);
        }

        [Fact]
        public void UpdateHighlights_WhenNodeIsSelected_HighlightsOnlyAdjacentConnections()
        {
            var service = new PlanGraphConnectionUiActionService();
            var selected = new PlanNodeViewModel { NodeId = "selected" };
            var neighbor = new PlanNodeViewModel { NodeId = "neighbor" };
            var other = new PlanNodeViewModel { NodeId = "other" };
            ConnectionViewModel adjacent = new()
            {
                Source = selected,
                Target = neighbor
            };
            ConnectionViewModel unrelated = new()
            {
                Source = neighbor,
                Target = other
            };

            service.UpdateHighlights(
                "selected",
                [adjacent, unrelated]);

            adjacent.IsHighlighted.Should().BeTrue();
            unrelated.IsHighlighted.Should().BeFalse();
        }

        [Fact]
        public void UpdateHighlights_WhenNoNodeIsSelected_HighlightsEveryConnection()
        {
            var service = new PlanGraphConnectionUiActionService();
            ConnectionViewModel connection = new()
            {
                Source = new PlanNodeViewModel { NodeId = "a" },
                Target = new PlanNodeViewModel { NodeId = "b" },
                IsHighlighted = false
            };

            service.UpdateHighlights(null, [connection]);

            connection.IsHighlighted.Should().BeTrue();
        }

        private static XDocument CreateTwoNodePlan()
        {
            return new XDocument(
                new XElement("ShowPlan",
                    new XElement("RelOp",
                        new XAttribute("NodeId", "0"),
                        new XElement("RelOp",
                            new XAttribute("NodeId", "1")))));
        }
    }
}
