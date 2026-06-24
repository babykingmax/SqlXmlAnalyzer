using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Refactoring
{
    public class MissingIndexSuggesterTests
    {
        [Fact]
        public void SuggestIndexes_SimpleEquality_ShouldRecommendEqualityKey()
        {
            // Arrange
            string sql = "SELECT Name, Age FROM Users WHERE UserID = 123";

            // Act
            var suggestions = MissingIndexSuggester.SuggestIndexes(sql);

            // Assert
            suggestions.Should().HaveCount(1);
            var sug = suggestions.First();
            sug.Table.Should().Be("Users");
            sug.KeyColumns.Should().ContainSingle(c => c.Name == "[UserID]" && c.Usage == "EQUALITY");
            sug.IncludeColumns.Should().ContainSingle(c => c.Name == "[Name]" && c.Usage == "INCLUDE");
            sug.IncludeColumns.Should().ContainSingle(c => c.Name == "[Age]" && c.Usage == "INCLUDE");
            sug.IncludeColumns.Should().NotContain(c => c.Name == "[UserID]");
        }

        [Fact]
        public void SuggestIndexes_SimpleInequality_ShouldRecommendInequalityKey()
        {
            // Arrange
            string sql = "SELECT Name FROM Users WHERE Age > 18";

            // Act
            var suggestions = MissingIndexSuggester.SuggestIndexes(sql);

            // Assert
            suggestions.Should().HaveCount(1);
            var sug = suggestions.First();
            sug.KeyColumns.Should().ContainSingle(c => c.Name == "[Age]" && c.Usage == "INEQUALITY");
            sug.IncludeColumns.Should().ContainSingle(c => c.Name == "[Name]" && c.Usage == "INCLUDE");
        }

        [Fact]
        public void SuggestIndexes_IsNullPredicate_ShouldRecommendCorrectUsage()
        {
            // Arrange & Act
            var sugNull = MissingIndexSuggester.SuggestIndexes("SELECT Age FROM Users WHERE Email IS NULL");
            var sugNotNull = MissingIndexSuggester.SuggestIndexes("SELECT Age FROM Users WHERE Email IS NOT NULL");

            // Assert
            sugNull.Should().HaveCount(1);
            sugNull.First().KeyColumns.Should().ContainSingle(c => c.Name == "[Email]" && c.Usage == "EQUALITY");

            sugNotNull.Should().HaveCount(1);
            sugNotNull.First().KeyColumns.Should().ContainSingle(c => c.Name == "[Email]" && c.Usage == "INEQUALITY");
        }

        [Fact]
        public void SuggestIndexes_BetweenPredicate_ShouldRecommendInequalityKey()
        {
            // Arrange
            string sql = "SELECT Email FROM Users WHERE CreatedDate BETWEEN '2026-01-01' AND '2026-06-30'";

            // Act
            var suggestions = MissingIndexSuggester.SuggestIndexes(sql);

            // Assert
            suggestions.Should().HaveCount(1);
            suggestions.First().KeyColumns.Should().ContainSingle(c => c.Name == "[CreatedDate]" && c.Usage == "INEQUALITY");
        }

        [Fact]
        public void SuggestIndexes_InPredicate_ShouldRecommendInequalityKey()
        {
            // Arrange
            string sql = "SELECT Email FROM Users WHERE StatusID IN (1, 2, 3)";

            // Act
            var suggestions = MissingIndexSuggester.SuggestIndexes(sql);

            // Assert
            suggestions.Should().HaveCount(1);
            suggestions.First().KeyColumns.Should().ContainSingle(c => c.Name == "[StatusID]" && c.Usage == "INEQUALITY");
        }

        [Fact]
        public void SuggestIndexes_ParenthesizedJoin_ShouldParseSuccessfully()
        {
            // Arrange
            string sql = "SELECT u.Name, o.OrderID FROM (Users u INNER JOIN Orders o ON u.UserID = o.UserID) WHERE u.Age > 20";

            // Act
            var suggestions = MissingIndexSuggester.SuggestIndexes(sql);

            // Assert
            suggestions.Should().NotBeEmpty();
        }

        [Fact]
        public void SuggestIndexes_CorrelatedSubquery_ShouldCorrectlyResolveOuterTableScope()
        {
            // Arrange
            string sql = "SELECT u.Name FROM Users u WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.UserID = u.UserID AND o.TotalAmount > 100)";

            // Act
            var suggestions = MissingIndexSuggester.SuggestIndexes(sql);

            // Assert
            suggestions.Should().NotBeEmpty();

            var usersSug = suggestions.FirstOrDefault(s => s.Table == "Users");
            usersSug.Should().NotBeNull();
            usersSug!.KeyColumns.Should().ContainSingle(c => c.Name == "[UserID]" && c.Usage == "EQUALITY");

            var ordersSug = suggestions.FirstOrDefault(s => s.Table == "Orders");
            ordersSug.Should().NotBeNull();
            ordersSug!.KeyColumns.Should().ContainSingle(c => c.Name == "[UserID]" && c.Usage == "EQUALITY");
            ordersSug!.KeyColumns.Should().ContainSingle(c => c.Name == "[TotalAmount]" && c.Usage == "INEQUALITY");
        }
    }
}
