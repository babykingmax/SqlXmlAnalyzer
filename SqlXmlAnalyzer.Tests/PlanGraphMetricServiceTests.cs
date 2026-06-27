using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphMetricServiceTests
    {
        [Theory]
        [InlineData(999, "999")]
        [InlineData(1000, "1K")]
        [InlineData(12345, "12.3K")]
        [InlineData(1_500_000, "1.5M")]
        public void FormatNumber_ReturnsCompactPlanGraphText(
            double value,
            string expected)
        {
            string result = PlanGraphMetricService.FormatNumber(value);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(512, "512 B")]
        [InlineData(1536, "1.5 KB")]
        [InlineData(1_572_864, "1.5 MB")]
        [InlineData(1_610_612_736, "1.5 GB")]
        public void FormatBytes_ReturnsCompactSizeText(
            double value,
            string expected)
        {
            string result = PlanGraphMetricService.FormatBytes(value);

            result.Should().Be(expected);
        }

        [Fact]
        public void CalculateLinkThickness_WhenMetricIsMissing_ReturnsMinimumWidth()
        {
            double result = PlanGraphMetricService.CalculateLinkThickness(0);

            result.Should().Be(1.5);
        }

        [Fact]
        public void CalculateLinkThickness_UsesCurrentTanhScale()
        {
            double result = PlanGraphMetricService.CalculateLinkThickness(9999);

            result.Should().BeApproximately(9.4967, 0.0001);
        }

        [Theory]
        [InlineData(null, 1.0)]
        [InlineData(0, 1.0)]
        [InlineData("1000", 4.8)]
        [InlineData(100_000_000, 12.8)]
        public void CalculateLegacyConverterThickness_PreservesConverterScale(
            object? value,
            double expected)
        {
            double result = PlanGraphMetricService.CalculateLegacyConverterThickness(value);

            result.Should().BeApproximately(expected, 0.0001);
        }

        [Fact]
        public void CalculateLegacyConverterThickness_CapsLargeValues()
        {
            double result =
                PlanGraphMetricService.CalculateLegacyConverterThickness(1_000_000_000L);

            result.Should().Be(14.0);
        }
    }
}
