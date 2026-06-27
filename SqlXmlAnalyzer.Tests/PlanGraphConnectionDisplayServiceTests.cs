using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphConnectionDisplayServiceTests
    {
        private readonly PlanGraphConnectionDisplayService _service = new();

        [Fact]
        public void CalculateRowsCount_WhenActualRowsExist_UsesActualRows()
        {
            double result = _service.CalculateRowsCount(
                CreateNode(estimatedRows: 100, actualRows: 250));

            result.Should().Be(250);
        }

        [Fact]
        public void CalculateRowsCount_WhenActualRowsAreMissing_UsesEstimatedRows()
        {
            double result = _service.CalculateRowsCount(
                CreateNode(estimatedRows: 100, actualRows: 0));

            result.Should().Be(100);
        }

        [Fact]
        public void CalculateDataSize_UsesSelectedRowCountAndAverageRowSize()
        {
            double result = _service.CalculateDataSize(
                CreateNode(
                    estimatedRows: 100,
                    actualRows: 250,
                    averageRowSize: 16));

            result.Should().Be(4000);
        }

        [Theory]
        [InlineData(PlanGraphConnectionMetricKind.RowCount, "250")]
        [InlineData(PlanGraphConnectionMetricKind.DataSize, "3.9 KB")]
        public void BuildLabel_ReturnsMetricText(
            PlanGraphConnectionMetricKind metricKind,
            string expected)
        {
            string result = _service.BuildLabel(
                metricKind,
                CreateNode(
                    estimatedRows: 100,
                    actualRows: 250,
                    averageRowSize: 16));

            result.Should().Be(expected);
        }

        [Fact]
        public void BuildToolTip_WhenSourceIsMissing_ReturnsUnknownFlowText()
        {
            string result = _service.BuildToolTip(null, "Nested Loops");

            result.Should().Be("未知数据流");
        }

        [Fact]
        public void BuildToolTip_WhenActualRowsExist_IncludesRuntimeMetrics()
        {
            string result = _service.BuildToolTip(
                CreateNode(
                    physicalOp: "Index Seek",
                    estimatedRows: 100,
                    actualRowsText: "650",
                    actualRows: 650,
                    averageRowSize: 32),
                "Nested Loops");

            result.Should().Contain("数据流: Index Seek ➔ Nested Loops");
            result.Should().Contain("预估行数: 100 (100)");
            result.Should().Contain("实际行数: 650 (650)");
            result.Should().Contain("平均行宽: 32 字节");
            result.Should().Contain("预估大小: 3.1 KB");
            result.Should().Contain("实际大小: 20.3 KB");
            result.Should().Contain("估算偏差: 6.50 倍");
            result.Should().Contain("严重低估");
        }

        [Fact]
        public void BuildToolTip_WhenActualRowsAreMissing_OmitsRuntimeMetrics()
        {
            string result = _service.BuildToolTip(
                CreateNode(
                    physicalOp: "Index Scan",
                    estimatedRows: 100,
                    actualRowsText: "",
                    actualRows: 0,
                    averageRowSize: 32),
                "Hash Match");

            result.Should().Contain("数据流: Index Scan ➔ Hash Match");
            result.Should().Contain("预估行数: 100 (100)");
            result.Should().Contain("预估大小: 3.1 KB");
            result.Should().NotContain("实际行数");
            result.Should().NotContain("估算偏差");
        }

        [Theory]
        [InlineData(0, 100, "", PlanGraphConnectionStrokeKey.Default)]
        [InlineData(650, 100, "650", PlanGraphConnectionStrokeKey.Red)]
        [InlineData(10, 100, "10", PlanGraphConnectionStrokeKey.Red)]
        [InlineData(250, 100, "250", PlanGraphConnectionStrokeKey.Orange)]
        [InlineData(40, 100, "40", PlanGraphConnectionStrokeKey.Orange)]
        [InlineData(100, 100, "100", PlanGraphConnectionStrokeKey.Green)]
        public void GetStrokeKey_ReturnsExpectedSkewColor(
            double actualRows,
            double estimatedRows,
            string actualRowsText,
            PlanGraphConnectionStrokeKey expected)
        {
            PlanGraphConnectionStrokeKey result = _service.GetStrokeKey(
                CreateNode(
                    estimatedRows: estimatedRows,
                    actualRowsText: actualRowsText,
                    actualRows: actualRows));

            result.Should().Be(expected);
        }

        private static PlanGraphConnectionNodeInfo CreateNode(
            string physicalOp = "Index Seek",
            double estimatedRows = 100,
            string? actualRowsText = "250",
            double actualRows = 250,
            double averageRowSize = 16)
        {
            return new PlanGraphConnectionNodeInfo(
                physicalOp,
                estimatedRows,
                actualRowsText,
                actualRows,
                averageRowSize);
        }
    }
}
