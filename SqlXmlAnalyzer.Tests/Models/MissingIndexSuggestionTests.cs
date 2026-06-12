using System.Collections.Generic;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Models
{
    public class MissingIndexSuggestionTests
    {
        [Fact]
        public void CreateIndexStatement_WithKeysAndIncludes_ShouldGenerateCorrectSql()
        {
            // Arrange
            var suggestion = new MissingIndexSuggestion
            {
                Schema = "[dbo]",
                Table = "[Employee]",
                KeyColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "[DepartmentID]", Usage = "EQUALITY" },
                    new IndexColumn { Name = "[HireDate]", Usage = "INEQUALITY" }
                },
                IncludeColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "[Salary]", Usage = "INCLUDE" },
                    new IndexColumn { Name = "[Bonus]", Usage = "INCLUDE" }
                }
            };

            // Act
            var sql = suggestion.CreateIndexStatement;

            // Assert
            sql.Should().Be("CREATE NONCLUSTERED INDEX [IX_Employee_DepartmentID] ON [dbo].[Employee] ([DepartmentID], [HireDate]) INCLUDE ([Salary], [Bonus])");
        }

        [Fact]
        public void CreateIndexStatement_EmptyKeys_ShouldReturnEmptyString()
        {
            // Arrange
            var suggestion = new MissingIndexSuggestion
            {
                Schema = "[dbo]",
                Table = "[Employee]",
                KeyColumns = new List<IndexColumn>(),
                IncludeColumns = new List<IndexColumn> { new IndexColumn { Name = "[Salary]", Usage = "INCLUDE" } }
            };

            // Act
            var sql = suggestion.CreateIndexStatement;

            // Assert
            sql.Should().BeEmpty();
        }

        [Fact]
        public void CreateIndexStatement_NoIncludes_ShouldNotHaveIncludeClause()
        {
            // Arrange
            var suggestion = new MissingIndexSuggestion
            {
                Schema = "[dbo]",
                Table = "[Employee]",
                KeyColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "[DepartmentID]", Usage = "EQUALITY" }
                },
                IncludeColumns = new List<IndexColumn>()
            };

            // Act
            var sql = suggestion.CreateIndexStatement;

            // Assert
            sql.Should().Be("CREATE NONCLUSTERED INDEX [IX_Employee_DepartmentID] ON [dbo].[Employee] ([DepartmentID])");
        }
    }
}
