using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphCostVisualServiceTests
    {
        private readonly PlanGraphCostVisualService _service = new();

        [Fact]
        public void GetStyle_WhenActivePercentIsZero_ReturnsColdVisuals()
        {
            PlanGraphCostVisualStyle result = _service.GetStyle(0);

            result.BackgroundTopColorHex.Should().Be("#FFFFFF");
            result.BackgroundBottomColorHex.Should().Be("#F5F7FA");
            result.BorderColorHex.Should().Be("#B0BEC5");
            result.BorderThickness.Should().Be(1.0);
            result.BadgeBackgroundColorHex.Should().Be("#CFD8DC");
            result.BadgeForegroundColorHex.Should().Be("#000000");
        }

        [Fact]
        public void GetStyle_WhenActivePercentIsOneHundred_ReturnsHotVisuals()
        {
            PlanGraphCostVisualStyle result = _service.GetStyle(100);

            result.BackgroundTopColorHex.Should().Be("#FFE6E6");
            result.BackgroundBottomColorHex.Should().Be("#FFBEBE");
            result.BorderColorHex.Should().Be("#D32F2F");
            result.BorderThickness.Should().Be(2.0);
            result.BadgeBackgroundColorHex.Should().Be("#EF5350");
            result.BadgeForegroundColorHex.Should().Be("#FFFFFF");
        }

        [Fact]
        public void GetStyle_WhenActivePercentExceedsOneHundred_ClampsGradient()
        {
            PlanGraphCostVisualStyle result = _service.GetStyle(125);

            result.BackgroundTopColorHex.Should().Be("#FFE6E6");
            result.BackgroundBottomColorHex.Should().Be("#FFBEBE");
            result.BorderColorHex.Should().Be("#D32F2F");
        }

        [Fact]
        public void GetStyle_WhenActivePercentIsNegative_ClampsGradient()
        {
            PlanGraphCostVisualStyle result = _service.GetStyle(-10);

            result.BackgroundTopColorHex.Should().Be("#FFFFFF");
            result.BackgroundBottomColorHex.Should().Be("#F5F7FA");
            result.BorderColorHex.Should().Be("#B0BEC5");
        }

        [Theory]
        [InlineData(14.99, "#CFD8DC", "#000000", 1.0)]
        [InlineData(15.0, "#FFB300", "#FFFFFF", 1.0)]
        [InlineData(29.99, "#FFB300", "#FFFFFF", 1.0)]
        [InlineData(30.0, "#FFB300", "#FFFFFF", 2.0)]
        [InlineData(39.99, "#FFB300", "#FFFFFF", 2.0)]
        [InlineData(40.0, "#EF5350", "#FFFFFF", 2.0)]
        public void GetStyle_PreservesBadgeAndBorderThresholds(
            double activePercent,
            string expectedBadgeBackground,
            string expectedBadgeForeground,
            double expectedBorderThickness)
        {
            PlanGraphCostVisualStyle result = _service.GetStyle(activePercent);

            result.BadgeBackgroundColorHex.Should().Be(expectedBadgeBackground);
            result.BadgeForegroundColorHex.Should().Be(expectedBadgeForeground);
            result.BorderThickness.Should().Be(expectedBorderThickness);
        }
    }
}
