using System;
using System.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class SqlDiffServiceTests
    {
        [Fact]
        public void IsEquivalentForDiff_IgnoresWhitespaceAndCase()
        {
            var service = new SqlDiffService();

            service.IsEquivalentForDiff("SELECT  * FROM dbo.T", "select*from dbo.t")
                .Should()
                .BeTrue();
        }

        [Fact]
        public void AlignLines_PairsAdjacentSingleLineChanges()
        {
            var service = new SqlDiffService();

            SqlAlignedLines result = service.AlignLines(
                new[] { "SELECT a", "FROM dbo.T" },
                new[] { "SELECT b", "FROM dbo.T" });

            result.Original.Should().Equal("SELECT a", "FROM dbo.T");
            result.Refactored.Should().Equal("SELECT b", "FROM dbo.T");
        }

        [Fact]
        public void AlignLines_UsesNullPlaceholdersForInsertedLines()
        {
            var service = new SqlDiffService();

            SqlAlignedLines result = service.AlignLines(
                new[] { "SELECT a", "FROM dbo.T" },
                new[] { "SELECT a", "WHERE a > 1", "FROM dbo.T" });

            result.Original.Should().Equal("SELECT a", null, "FROM dbo.T");
            result.Refactored.Should().Equal("SELECT a", "WHERE a > 1", "FROM dbo.T");
        }

        [Fact]
        public void AlignLines_WhenInputIsLarge_AlignsByPosition()
        {
            var service = new SqlDiffService();
            string[] original = Enumerable.Range(0, 1001)
                .Select(i => $"A{i}")
                .ToArray();
            string[] refactored = Enumerable.Range(0, 1003)
                .Select(i => $"B{i}")
                .ToArray();

            SqlAlignedLines result = service.AlignLines(original, refactored);

            result.Original.Should().HaveCount(1003);
            result.Refactored.Should().HaveCount(1003);
            result.Original[0].Should().Be("A0");
            result.Refactored[0].Should().Be("B0");
            result.Original[^1].Should().BeNull();
            result.Refactored[^1].Should().Be("B1002");
        }

        [Fact]
        public void TokenizeLine_ClassifiesSqlTokens()
        {
            var service = new SqlDiffService();

            SqlDiffToken[] tokens = service.TokenizeLine("SELECT 'x' -- note").ToArray();

            tokens.Should().Contain(new SqlDiffToken("SELECT", SqlDiffTokenKind.Keyword, 0, 6));
            tokens.Should().Contain(new SqlDiffToken("'x'", SqlDiffTokenKind.StringLiteral, 7, 3));
            tokens.Should().Contain(token =>
                token.Text == "-- note" &&
                token.Kind == SqlDiffTokenKind.Comment);
        }

        [Fact]
        public void GetLineStartOffsets_HandlesWindowsAndUnixNewlines()
        {
            var service = new SqlDiffService();

            service.GetLineStartOffsets("a\r\nbb\nccc")
                .Should()
                .Equal(0, 3, 6);
        }

        [Fact]
        public void AlignLines_WhenOriginalIsNull_Throws()
        {
            var service = new SqlDiffService();

            Action act = () => service.AlignLines(null!, Array.Empty<string>());

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
