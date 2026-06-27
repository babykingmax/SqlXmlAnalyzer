using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphNodeDisplayServiceTests
    {
        private readonly PlanGraphNodeDisplayService _service = new();

        [Theory]
        [InlineData("True", "8", "1 - 8")]
        [InlineData("True", "8", "1-8")]
        public void IsFullPartitionScan_WhenRangeCoversAllPartitions_ReturnsTrue(
            string partitioned,
            string partitionCount,
            string partitionRange)
        {
            bool result = _service.IsFullPartitionScan(
                partitioned,
                partitionCount,
                partitionRange);

            result.Should().BeTrue();
        }

        [Theory]
        [InlineData("False", "8", "1 - 8")]
        [InlineData("true", "8", "1 - 8")]
        [InlineData("True", "", "1 - 8")]
        [InlineData("True", "8", "2 - 8")]
        public void IsFullPartitionScan_WhenPartitionMetadataDoesNotMatch_ReturnsFalse(
            string partitioned,
            string partitionCount,
            string partitionRange)
        {
            bool result = _service.IsFullPartitionScan(
                partitioned,
                partitionCount,
                partitionRange);

            result.Should().BeFalse();
        }

        [Fact]
        public void GetPartitionColors_WhenFullPartitionScan_ReturnsWarningColors()
        {
            string rangeColor =
                _service.GetPartitionRangeColor("True", "4", "1-4");
            string labelColor =
                _service.GetPartitionLabelColor("True", "4", "1-4");

            rangeColor.Should().Be("#FF0000");
            labelColor.Should().Be("#FF0000");
        }

        [Fact]
        public void GetPartitionColors_WhenNotFullPartitionScan_ReturnsNeutralColors()
        {
            string rangeColor =
                _service.GetPartitionRangeColor("True", "4", "2-4");
            string labelColor =
                _service.GetPartitionLabelColor("True", "4", "2-4");

            rangeColor.Should().Be("#263238");
            labelColor.Should().Be("#546E7A");
        }

        [Theory]
        [InlineData(null, "Collapsed")]
        [InlineData("", "Collapsed")]
        [InlineData("value", "Visible")]
        public void GetTextVisibility_ReturnsExpectedVisibility(
            string? value,
            string expected)
        {
            string result = _service.GetTextVisibility(value);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(true, "Visible")]
        [InlineData(false, "Collapsed")]
        public void GetBooleanVisibility_ReturnsExpectedVisibility(
            bool value,
            string expected)
        {
            string result = _service.GetBooleanVisibility(value);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("True", "Visible")]
        [InlineData("true", "Collapsed")]
        [InlineData("False", "Collapsed")]
        public void GetPartitionInfoVisibility_PreservesExistingCaseSensitivity(
            string partitioned,
            string expected)
        {
            string result = _service.GetPartitionInfoVisibility(partitioned);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Critical", "#D32F2F")]
        [InlineData("Warning", "#F57C00")]
        [InlineData("Info", "Transparent")]
        [InlineData("Unknown", "Transparent")]
        public void GetNodeSeverityColor_ReturnsExpectedColor(
            string nodeSeverity,
            string expected)
        {
            string result = _service.GetNodeSeverityColor(nodeSeverity);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Info", "0")]
        [InlineData("Warning", "2")]
        [InlineData("Critical", "2")]
        [InlineData("Unknown", "2")]
        public void GetNodeSeverityBorderThickness_ReturnsExpectedThickness(
            string nodeSeverity,
            string expected)
        {
            string result = _service.GetNodeSeverityBorderThickness(nodeSeverity);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(true, "", "Visible")]
        [InlineData(false, "warning", "Visible")]
        [InlineData(false, "", "Collapsed")]
        public void GetExtraInfoVisibility_ReturnsExpectedVisibility(
            bool isParallel,
            string warnings,
            string expected)
        {
            string result =
                _service.GetExtraInfoVisibility(isParallel, warnings);

            result.Should().Be(expected);
        }
    }
}
