using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Services;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class SqlSemanticAffectedRowsTests
{
    [Theory]
    [InlineData(new int[] { }, -1)]
    [InlineData(new[] { -1, -1 }, -1)]
    [InlineData(new[] { 1, 2 }, 3)]
    [InlineData(new[] { 4, -1 }, 4)]
    [InlineData(new[] { -1, 4 }, 4)]
    [InlineData(new[] { -1, 0, -1 }, 0)]
    [InlineData(new[] { 0, 2, -1, 1 }, 3)]
    [InlineData(new[] { int.MaxValue, -1, 0 }, int.MaxValue)]
    public void Combine_PreservesNotApplicableAndCountsOnlyDml(int[] batches, int expected)
    {
        batches.Aggregate(-1, SqlSemanticAffectedRows.Combine).Should().Be(expected);
    }

    [Theory]
    [InlineData(-2, 1)]
    [InlineData(1, -2)]
    [InlineData(int.MaxValue, 1)]
    public void Combine_InvalidOrOverflowingEvidenceFailsInsteadOfWrapping(int accumulated, int batch)
    {
        Action combine = () => SqlSemanticAffectedRows.Combine(accumulated, batch);
        combine.Should().Throw<InvalidDataException>();
    }
}
