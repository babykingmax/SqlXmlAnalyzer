using System;
using System.Windows;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphPanInteractionServiceTests
    {
        private readonly PlanGraphPanInteractionService _service = new();

        [Fact]
        public void Begin_ReturnsPanningStateWithPointerPosition()
        {
            PlanGraphPanState state =
                _service.Begin(new Point(100, 80));

            state.IsPanning.Should().BeTrue();
            state.LastPointerPosition.Should().Be(new Point(100, 80));
        }

        [Fact]
        public void Pan_WhenStateIsPanning_MovesViewportOppositePointerDelta()
        {
            PlanGraphPanState state =
                new(IsPanning: true, new Point(100, 80));

            PlanGraphPanUpdate? update =
                _service.Pan(
                    state,
                    currentPointerPosition: new Point(125, 60),
                    currentViewportLocation: new Point(20, 30),
                    viewportZoom: 2.0);

            update.Should().NotBeNull();
            update!.ViewportLocation.X.Should().BeApproximately(7.5, 0.0001);
            update.ViewportLocation.Y.Should().BeApproximately(40, 0.0001);
            update.State.IsPanning.Should().BeTrue();
            update.State.LastPointerPosition.Should().Be(new Point(125, 60));
        }

        [Fact]
        public void Pan_WhenStateIsNotPanning_ReturnsNull()
        {
            PlanGraphPanUpdate? update =
                _service.Pan(
                    new PlanGraphPanState(
                        IsPanning: false,
                        LastPointerPosition: new Point(100, 80)),
                    currentPointerPosition: new Point(125, 60),
                    currentViewportLocation: new Point(20, 30),
                    viewportZoom: 1.0);

            update.Should().BeNull();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Pan_WhenViewportZoomIsInvalid_Throws(double viewportZoom)
        {
            Action act = () => _service.Pan(
                new PlanGraphPanState(
                    IsPanning: true,
                    LastPointerPosition: new Point(100, 80)),
                currentPointerPosition: new Point(125, 60),
                currentViewportLocation: new Point(20, 30),
                viewportZoom: viewportZoom);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void End_ReturnsNonPanningStateAndPreservesLastPointerPosition()
        {
            PlanGraphPanState result =
                _service.End(new PlanGraphPanState(
                    IsPanning: true,
                    LastPointerPosition: new Point(125, 60)));

            result.IsPanning.Should().BeFalse();
            result.LastPointerPosition.Should().Be(new Point(125, 60));
        }
    }
}
