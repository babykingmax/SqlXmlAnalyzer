using System;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockStepBadgeServiceTests
    {
        [Fact]
        public void PlaceBadge_ReturnsStepTextAndOffsetMidpoint()
        {
            var service = new DeadlockStepBadgeService();

            DeadlockStepBadgePlacement placement = service.PlaceBadge(
                stepNumber: 12,
                x1: 10,
                y1: 20,
                x2: 110,
                y2: 220);

            placement.Text.Should().Be("12");
            placement.Left.Should().Be(70);
            placement.Top.Should().Be(105);
        }

        [Fact]
        public void PlaceBadge_WhenCoordinatesAreNegative_StillUsesMidpoint()
        {
            var service = new DeadlockStepBadgeService();

            DeadlockStepBadgePlacement placement = service.PlaceBadge(
                stepNumber: 1,
                x1: -20,
                y1: -10,
                x2: 20,
                y2: 30);

            placement.Left.Should().Be(10);
            placement.Top.Should().Be(-5);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void PlaceBadge_WhenStepNumberIsNotPositive_Throws(int stepNumber)
        {
            var service = new DeadlockStepBadgeService();

            Action act = () => service.PlaceBadge(stepNumber, 0, 0, 10, 10);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
