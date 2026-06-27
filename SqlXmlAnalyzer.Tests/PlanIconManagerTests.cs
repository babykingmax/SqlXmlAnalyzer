using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanIconManagerTests
    {
        [Theory]
        [InlineData("Hash Match", "icon-hash-match.png")]
        [InlineData("Merge Join", "icon-merge-join.png")]
        [InlineData("Nested Loops", "icon-nested-loops.png")]
        [InlineData("Parallelism", "icon-parallelism.png")]
        [InlineData("Stream Aggregate", "icon-aggregate.png")]
        [InlineData("Compute Scalar", "icon-compute-scalar.png")]
        [InlineData("Key Lookup", "icon-key-lookup.png")]
        [InlineData("Clustered Index Scan", "icon-clustered-index-scan.png")]
        [InlineData("Clustered Index Seek", "icon-clustered-index-seek.png")]
        [InlineData("Index Scan", "icon-nonclustered-index-scan.png")]
        [InlineData("Index Seek", "icon-nonclustered-index-seek.png")]
        [InlineData("Table Scan", "icon-table-scan.png")]
        [InlineData("Table Valued Function", "icon-table-valued-function.png")]
        public void GetIconFileName_MapsKnownOperatorsToIconFiles(
            string operatorName,
            string expected)
        {
            string? result = PlanIconManager.GetIconFileName(operatorName);

            result.Should().Be(expected);
        }

        [Fact]
        public void GetIconFileName_WhenOperatorIsUnknown_NormalizesName()
        {
            string? result = PlanIconManager.GetIconFileName("Sequence_Project");

            result.Should().Be("icon-sequence-project.png");
        }

        [Fact]
        public void GetIconFileName_WhenOperatorIsEmpty_ReturnsNull()
        {
            string? result = PlanIconManager.GetIconFileName("");

            result.Should().BeNull();
        }
    }
}
