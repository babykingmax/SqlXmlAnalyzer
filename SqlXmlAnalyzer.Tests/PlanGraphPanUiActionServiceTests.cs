using System.Windows;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphPanUiActionServiceTests
    {
        [Fact]
        public void IsGraphItemDataContext_WhenDataContextIsNode_ReturnsTrue()
        {
            bool result =
                PlanGraphPanUiActionService.IsGraphItemDataContext(
                    new PlanNodeViewModel());

            result.Should().BeTrue();
        }

        [Fact]
        public void IsGraphItemDataContext_WhenDataContextIsConnection_ReturnsTrue()
        {
            bool result =
                PlanGraphPanUiActionService.IsGraphItemDataContext(
                    new ConnectionViewModel());

            result.Should().BeTrue();
        }

        [Fact]
        public void IsGraphItemDataContext_WhenDataContextIsOtherObject_ReturnsFalse()
        {
            bool result =
                PlanGraphPanUiActionService.IsGraphItemDataContext(
                    new object());

            result.Should().BeFalse();
        }

        [Fact]
        public void BeginPan_WhenOriginalSourceIsBackground_CapturesMouseAndStartsPanning()
        {
            var service = new PlanGraphPanUiActionService();
            bool captured = false;

            Core.Services.PlanGraphPanState state =
                service.BeginPan(
                    new object(),
                    new Point(10, 10),
                    () => captured = true,
                    new Core.Services.PlanGraphPanState(false, new Point()));

            captured.Should().BeTrue();
            state.IsPanning.Should().BeTrue();
            state.LastPointerPosition.Should().Be(new Point(10, 10));
        }

        [Fact]
        public void Pan_WhenPanning_UpdatesViewportAndReturnsNewState()
        {
            var service = new PlanGraphPanUiActionService();
            Point viewport = new(20, 30);

            Core.Services.PlanGraphPanState state =
                service.Pan(
                    new Core.Services.PlanGraphPanState(true, new Point(100, 80)),
                    new Point(120, 60),
                    viewport,
                    2.0,
                    point => viewport = point);

            viewport.X.Should().BeApproximately(10, 0.0001);
            viewport.Y.Should().BeApproximately(40, 0.0001);
            state.LastPointerPosition.Should().Be(new Point(120, 60));
        }

        [Fact]
        public void EndPan_WhenPanning_ReleasesMouseAndEndsState()
        {
            var service = new PlanGraphPanUiActionService();
            bool released = false;

            Core.Services.PlanGraphPanState state =
                service.EndPan(
                    new Core.Services.PlanGraphPanState(true, new Point(5, 6)),
                    () => released = true);

            released.Should().BeTrue();
            state.IsPanning.Should().BeFalse();
            state.LastPointerPosition.Should().Be(new Point(5, 6));
        }
    }
}
