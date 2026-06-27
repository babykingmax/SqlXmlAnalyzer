using System;
using System.Windows;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockNodeDragResult(
        Point Position,
        Point LastPointer);

    public sealed class DeadlockNodeDragService
    {
        public DeadlockNodeDragResult Drag(
            Point currentNodePosition,
            Point previousPointer,
            Point currentPointer)
        {
            double deltaX = currentPointer.X - previousPointer.X;
            double deltaY = currentPointer.Y - previousPointer.Y;

            return new DeadlockNodeDragResult(
                new Point(
                    currentNodePosition.X + deltaX,
                    currentNodePosition.Y + deltaY),
                currentPointer);
        }

        public Point NormalizeCanvasPosition(double left, double top)
        {
            return new Point(
                double.IsNaN(left) ? 0 : left,
                double.IsNaN(top) ? 0 : top);
        }
    }
}
