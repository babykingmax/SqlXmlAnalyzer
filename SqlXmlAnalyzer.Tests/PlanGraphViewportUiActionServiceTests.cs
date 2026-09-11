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
        public void ResetView_WhenNodesExist_DoesNotMoveNodes()
        {
            var service = new PlanGraphViewportUiActionService();
            var firstNode = new PlanNodeViewModel
            {
                Location = new Point(25, 40)
            };

            service.ResetView(_ => { }, [firstNode]);

            firstNode.Location.Should().Be(new Point(25, 40));
        }

        [Fact]
        public void CalculateFitZoom_ContainsTheWholeGraphWithPadding()
        {
            double zoom = PlanGraphViewportUiActionService.CalculateFitZoom(new Rect(50, 70, 2000, 600), new Size(800, 400));
            (2000 * zoom).Should().BeLessThanOrEqualTo(768);
            (600 * zoom).Should().BeLessThanOrEqualTo(368);
            zoom.Should().BeGreaterThan(0.1);
        }

        [Fact]
        public void CalculateFitZoom_SmallGraphDoesNotUpscale()
        {
            PlanGraphViewportUiActionService.CalculateFitZoom(new Rect(0, 0, 230, 70), new Size(800, 600)).Should().Be(1);
            PlanGraphViewportUiActionService.CalculateFitZoom(Rect.Empty, new Size(800, 600)).Should().Be(1);
        }

        [Fact]
        public void CalculateFitZoom_WhenInputsAreInvalid_DoesNotProduceNonFiniteOrZeroZoom()
        {
            PlanGraphViewportUiActionService.CalculateFitZoom(new Rect(0, 0, double.PositiveInfinity, 50), new Size(800, 600)).Should().Be(1);
            PlanGraphViewportUiActionService.CalculateFitZoom(new Rect(0, 0, 50, 50), new Size(double.PositiveInfinity, 600)).Should().Be(1);
            PlanGraphViewportUiActionService.CalculateFitZoom(new Rect(0, 0, 50, 50), new Size(0, 0)).Should().Be(1);
            double zoom = PlanGraphViewportUiActionService.CalculateFitZoom(new Rect(0, 0, 260, 8000), new Size(200, 24));
            zoom.Should().BeGreaterThan(0).And.BeLessThan(0.02);
            (8000 * zoom).Should().BeLessThanOrEqualTo(12);
        }
    }
}
