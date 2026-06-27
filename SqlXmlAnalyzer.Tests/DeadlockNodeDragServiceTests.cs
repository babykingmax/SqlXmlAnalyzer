using System.Windows;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockNodeDragServiceTests
    {
        [Fact]
        public void Drag_AddsPointerDeltaToNodePosition()
        {
            var service = new DeadlockNodeDragService();

            DeadlockNodeDragResult result = service.Drag(
                currentNodePosition: new Point(100, 80),
                previousPointer: new Point(20, 30),
                currentPointer: new Point(45, 10));

            result.Position.Should().Be(new Point(125, 60));
            result.LastPointer.Should().Be(new Point(45, 10));
        }

        [Fact]
        public void Drag_WhenPointerMovesNegative_UpdatesPosition()
        {
            var service = new DeadlockNodeDragService();

            DeadlockNodeDragResult result = service.Drag(
                currentNodePosition: new Point(100, 80),
                previousPointer: new Point(45, 10),
                currentPointer: new Point(20, 30));

            result.Position.Should().Be(new Point(75, 100));
            result.LastPointer.Should().Be(new Point(20, 30));
        }

        [Fact]
        public void NormalizeCanvasPosition_WhenCanvasValuesAreSet_ReturnsPosition()
        {
            var service = new DeadlockNodeDragService();

            Point position = service.NormalizeCanvasPosition(120, 75);

            position.Should().Be(new Point(120, 75));
        }

        [Fact]
        public void NormalizeCanvasPosition_WhenCanvasValuesAreUnset_ReturnsOriginForNaN()
        {
            var service = new DeadlockNodeDragService();

            Point position = service.NormalizeCanvasPosition(double.NaN, double.NaN);

            position.Should().Be(new Point(0, 0));
        }
    }
}
