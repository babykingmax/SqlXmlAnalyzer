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
            sql.Should().Be("CREATE NONCLUSTERED INDEX [IX_Employee_DepartmentID_HireDate]\n" +
                            "ON [dbo].[Employee] ([DepartmentID], [HireDate])\n" +
                            "INCLUDE ([Salary], [Bonus])\n" +
                            "WITH (ONLINE = ON, DATA_COMPRESSION = PAGE, SORT_IN_TEMPDB = ON);");
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
            sql.Should().Be("CREATE NONCLUSTERED INDEX [IX_Employee_DepartmentID]\n" +
                            "ON [dbo].[Employee] ([DepartmentID])\n" +
                            "WITH (ONLINE = ON, DATA_COMPRESSION = PAGE, SORT_IN_TEMPDB = ON);");
        }

        [Fact]
        public void Generate_WithCustomOptions_ShouldFormatOptionsCorrectly()
        {
            // Arrange
            var suggestion = new MissingIndexSuggestion
            {
                Schema = "sales",
                Table = "Orders",
                KeyColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "OrderID", Usage = "EQUALITY" },
                    new IndexColumn { Name = "CustomerID", Usage = "EQUALITY" }
                }
            };
            var options = new SqlXmlAnalyzer.Core.Refactoring.IndexDdlOptions
            {
                Online = false,
                DataCompression = "ROW",
                SortInTempDb = false,
                MaxDop = 4
            };

            // Act
            var sql = SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler.Generate(suggestion, options);

            // Assert
            sql.Should().Be("CREATE NONCLUSTERED INDEX [IX_Orders_OrderID_CustomerID]\n" +
                            "ON [sales].[Orders] ([OrderID], [CustomerID])\n" +
                            "WITH (ONLINE = OFF, DATA_COMPRESSION = ROW, SORT_IN_TEMPDB = OFF, MAXDOP = 4);");
        }
    }
}
