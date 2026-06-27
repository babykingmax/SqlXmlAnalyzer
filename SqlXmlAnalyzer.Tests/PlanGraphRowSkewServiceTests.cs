using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphRowSkewServiceTests
    {
        private readonly PlanGraphRowSkewService _service = new();

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        [InlineData(-1, 100)]
        public void Analyze_WhenRowsAreMissing_ReturnsNeutralResult(
            double actualRows,
            double estimatedRows)
        {
            PlanGraphRowSkewResult result =
                _service.Analyze(actualRows, estimatedRows);

            result.BrushKey.Should().Be(PlanGraphRowSkewBrushKey.DimGray);
            result.Warning.Should().BeEmpty();
        }

        [Theory]
        [InlineData(600, 100, "↑↑ 严重高估")]
        [InlineData(350, 100, "↑ 高估")]
        public void Analyze_WhenHighSkewIsSevere_ReturnsRedBrush(
            double actualRows,
            double estimatedRows,
            string expectedWarning)
        {
            PlanGraphRowSkewResult result =
                _service.Analyze(actualRows, estimatedRows);

            result.BrushKey.Should().Be(PlanGraphRowSkewBrushKey.DarkRed);
            result.Warning.Should().Be(expectedWarning);
        }

        [Fact]
        public void Analyze_WhenHighSkewIsModerate_ReturnsOrangeBrushAndWarning()
        {
            PlanGraphRowSkewResult result = _service.Analyze(260, 100);

            result.BrushKey.Should().Be(PlanGraphRowSkewBrushKey.DarkOrange);
            result.Warning.Should().Be("↑ 高估");
        }

        [Fact]
        public void Analyze_WhenVisualSkewHasNoWarning_ReturnsOrangeBrushOnly()
        {
            PlanGraphRowSkewResult result = _service.Analyze(200, 100);

            result.BrushKey.Should().Be(PlanGraphRowSkewBrushKey.DarkOrange);
            result.Warning.Should().BeEmpty();
        }

        [Theory]
        [InlineData(10, 100, "↓↓ 严重低估")]
        [InlineData(25, 100, "↓ 低估")]
        public void Analyze_WhenLowSkewIsSevere_ReturnsRedBrush(
            double actualRows,
            double estimatedRows,
            string expectedWarning)
        {
            PlanGraphRowSkewResult result =
                _service.Analyze(actualRows, estimatedRows);

            result.BrushKey.Should().Be(PlanGraphRowSkewBrushKey.DarkRed);
            result.Warning.Should().Be(expectedWarning);
        }

        [Fact]
        public void Analyze_WhenLowSkewIsModerate_ReturnsOrangeBrushAndWarning()
        {
            PlanGraphRowSkewResult result = _service.Analyze(40, 100);

            result.BrushKey.Should().Be(PlanGraphRowSkewBrushKey.DarkOrange);
            result.Warning.Should().Be("↓ 低估");
        }

        [Fact]
        public void Analyze_WhenRowsMatch_ReturnsHealthyGreenBrush()
        {
            PlanGraphRowSkewResult result = _service.Analyze(100, 100);

            result.BrushKey.Should().Be(PlanGraphRowSkewBrushKey.HealthyGreen);
            result.Warning.Should().BeEmpty();
        }
    }
}
