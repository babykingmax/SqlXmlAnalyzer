using System.Windows;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphViewportUiActionServiceTests
    {
        [Fact]
        public void ResetView_AlwaysSetsZoomToOne()
        {
            var service = new PlanGraphViewportUiActionService();
            double zoom = 2.5;

            service.ResetView(value => zoom = value, []);

            zoom.Should().Be(1.0);
        }

        [Fact]
        public void ResetView_WhenNodesExist_RestoresFirstNodeLocationAfterNudge()
        {
            var service = new PlanGraphViewportUiActionService();
            var firstNode = new PlanNodeViewModel
            {
                Location = new Point(25, 40)
            };

            service.ResetView(_ => { }, [firstNode]);

            firstNode.Location.Should().Be(new Point(25, 40));
        }
    }
}
