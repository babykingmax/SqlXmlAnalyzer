using System;
using System.Globalization;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockStepBadgePlacement(
        string Text,
        double Left,
        double Top);

    public sealed class DeadlockStepBadgeService
    {
        private const double HorizontalOffset = 10;
        private const double VerticalOffset = -15;

        public DeadlockStepBadgePlacement PlaceBadge(
            int stepNumber,
            double x1,
            double y1,
            double x2,
            double y2)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepNumber);

            return new DeadlockStepBadgePlacement(
                stepNumber.ToString(CultureInfo.InvariantCulture),
                (x1 + x2) / 2 + HorizontalOffset,
                (y1 + y2) / 2 + VerticalOffset);
        }
    }
}
