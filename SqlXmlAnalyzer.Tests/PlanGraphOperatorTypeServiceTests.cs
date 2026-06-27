using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphOperatorTypeServiceTests
    {
        [Theory]
        [InlineData("Clustered Index Scan", "", "Scan")]
        [InlineData("Index Seek", "", "Seek")]
        [InlineData("Bookmark Lookup", "", "Seek")]
        [InlineData("Hash Match", "", "Join")]
        [InlineData("Nested Loops", "", "Join")]
        [InlineData("Merge Join", "", "Join")]
        [InlineData("Parallelism", "", "Parallelism")]
        [InlineData("Exchange", "Gather Streams", "Parallelism")]
        [InlineData("Sort", "", "Sort")]
        [InlineData("Top", "", "Sort")]
        [InlineData("Table Spool", "", "Spool")]
        [InlineData("Compute Scalar", "", "Compute")]
        [InlineData("Sequence Project", "", "Other")]
        public void DetectOperatorType_ReturnsExpectedCategory(
            string physicalOp,
            string logicalOp,
            string expected)
        {
            var service = new PlanGraphOperatorTypeService();

            string result = service.DetectOperatorType(physicalOp, logicalOp);

            result.Should().Be(expected);
        }

        [Fact]
        public void DetectOperatorType_WhenPhysicalIsMissing_UsesLogicalText()
        {
            var service = new PlanGraphOperatorTypeService();

            string result = service.DetectOperatorType(null, "Distribute Streams");

            result.Should().Be("Parallelism");
        }
    }
}
