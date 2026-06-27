using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphOperatorVisualServiceTests
    {
        private readonly PlanGraphOperatorVisualService _service = new();

        [Theory]
        [InlineData("Scan", "#2962FF")]
        [InlineData("Seek", "#00C853")]
        [InlineData("Join", "#FF6D00")]
        [InlineData("Parallelism", "#D500F9")]
        [InlineData("Sort", "#FF1744")]
        [InlineData("Spool", "#00B8D4")]
        [InlineData("Compute", "#FFC400")]
        [InlineData("Other", "#607D8B")]
        public void GetStyle_ReturnsStableAccentColor(
            string operatorType,
            string expectedColor)
        {
            PlanGraphOperatorVisualStyle result = _service.GetStyle(operatorType);

            result.AccentColorHex.Should().Be(expectedColor);
        }

        [Theory]
        [InlineData("Scan", "M4 4h16v16H4V4")]
        [InlineData("Seek", "M15.5 14h-.79")]
        [InlineData("Join", "M15 16c0-3.31")]
        [InlineData("Parallelism", "M14 4l2.29")]
        [InlineData("Sort", "M3 18h6")]
        [InlineData("Spool", "M12 2C6.48 2 2 3.79")]
        [InlineData("Compute", "M19 3H5")]
        public void GetStyle_ReturnsExpectedGeometry(
            string operatorType,
            string expectedGeometryPrefix)
        {
            PlanGraphOperatorVisualStyle result = _service.GetStyle(operatorType);

            result.GeometryData.Should().StartWith(expectedGeometryPrefix);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Unknown")]
        public void GetStyle_WhenOperatorTypeIsUnknown_ReturnsDefaultStyle(
            string? operatorType)
        {
            PlanGraphOperatorVisualStyle result = _service.GetStyle(operatorType);

            result.AccentColorHex.Should().Be("#607D8B");
            result.GeometryData.Should().StartWith("M12 2C6.48");
        }
    }
}
