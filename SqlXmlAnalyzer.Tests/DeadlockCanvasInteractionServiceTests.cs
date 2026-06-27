using System;
using System.Windows;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockCanvasInteractionServiceTests
    {
        [Fact]
        public void ZoomAt_WhenWheelDeltaIsPositive_ZoomsAroundAnchor()
        {
            var service = new DeadlockCanvasInteractionService();

            DeadlockCanvasTransformState? state = service.ZoomAt(
                wheelDelta: 120,
                anchor: new Point(200, 150),
                currentScale: 1.0,
                translateX: 20,
                translateY: 30);

            state.Should().NotBeNull();
            state!.Scale.Should().BeApproximately(1.1, 0.0001);
            state.TranslateX.Should().BeApproximately(2, 0.0001);
            state.TranslateY.Should().BeApproximately(18, 0.0001);
        }

        [Fact]
        public void ZoomAt_WhenWheelDeltaIsNegative_ZoomsOutAroundAnchor()
        {
            var service = new DeadlockCanvasInteractionService();

            DeadlockCanvasTransformState? state = service.ZoomAt(
                wheelDelta: -120,
                anchor: new Point(200, 150),
                currentScale: 2.0,
                translateX: 20,
                translateY: 30);

            state.Should().NotBeNull();
            state!.Scale.Should().BeApproximately(1.8, 0.0001);
            state.TranslateX.Should().BeApproximately(38, 0.0001);
            state.TranslateY.Should().BeApproximately(42, 0.0001);
        }

        [Theory]
        [InlineData(120, 9.2)]
        [InlineData(-120, 0.1)]
        public void ZoomAt_WhenScaleWouldExceedBounds_ReturnsNull(
            int wheelDelta,
            double currentScale)
        {
            var service = new DeadlockCanvasInteractionService();

            DeadlockCanvasTransformState? state = service.ZoomAt(
                wheelDelta,
                new Point(10, 10),
                currentScale,
                translateX: 0,
                translateY: 0);

            state.Should().BeNull();
        }

        [Fact]
        public void ZoomAt_WhenScaleIsInvalid_Throws()
        {
            var service = new DeadlockCanvasInteractionService();

            Action act = () => service.ZoomAt(
                wheelDelta: 120,
                anchor: new Point(10, 10),
                currentScale: 0,
                translateX: 0,
                translateY: 0);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void Pan_AddsPointerDeltaToTranslation()
        {
            var service = new DeadlockCanvasInteractionService();

            DeadlockCanvasTransformState state = service.Pan(
                currentScale: 1.5,
                translateX: 20,
                translateY: 30,
                previous: new Point(100, 80),
                current: new Point(125, 60));

            state.Scale.Should().Be(1.5);
            state.TranslateX.Should().Be(45);
            state.TranslateY.Should().Be(10);
        }
    }
}
