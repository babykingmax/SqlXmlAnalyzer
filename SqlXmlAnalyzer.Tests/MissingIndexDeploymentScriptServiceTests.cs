using System;
using System.Collections.Generic;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class MissingIndexDeploymentScriptServiceTests
    {
        [Fact]
        public void BuildDeploymentBundle_ReturnsCreateAndRollbackSections()
        {
            var service = new MissingIndexDeploymentScriptService();
            MissingIndexSuggestion suggestion = CreateSuggestion();

            string bundle = service.BuildDeploymentBundle(suggestion);

            bundle.Should().Contain("SQL Server Missing Index Deployment Bundle");
            bundle.Should().Contain(" * Table:  [Orders]");
            bundle.Should().Contain(" * Schema: [sales]");
            bundle.Should().Contain(" * Impact: 89.12%");
            bundle.Should().Contain(" * Score:  92/100");
            bundle.Should().Contain("-- === 1. DEPLOYMENT DDL (CREATE INDEX) ===");
            bundle.Should().Contain("BEGIN TRANSACTION;");
            bundle.Should().Contain("CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId_OrderDate]");
            bundle.Should().Contain("-- === 2. ROLLBACK DDL (DROP INDEX) ===");
            bundle.Should().Contain("DROP INDEX [IX_Orders_CustomerId_OrderDate] ON [sales].[Orders];");
        }

        [Fact]
        public void BuildDeploymentBundle_WhenSchemaIsEmpty_OmitsSchemaLine()
        {
            var service = new MissingIndexDeploymentScriptService();
            MissingIndexSuggestion suggestion = CreateSuggestion();
            suggestion.Schema = string.Empty;

            string bundle = service.BuildDeploymentBundle(suggestion);

            bundle.Should().NotContain(" * Schema:");
            bundle.Should().Contain("ON [Orders]");
        }

        [Fact]
        public void BuildDeploymentBundle_WhenSuggestionIsNull_Throws()
        {
            var service = new MissingIndexDeploymentScriptService();

            Action act = () => service.BuildDeploymentBundle(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        private static MissingIndexSuggestion CreateSuggestion()
        {
            return new MissingIndexSuggestion
            {
                Schema = "[sales]",
                Table = "[Orders]",
                Impact = 89.123,
                Score = 92,
                KeyColumns = new List<IndexColumn>
                {
                    new() { Name = "[CustomerId]", Usage = "EQUALITY" },
                    new() { Name = "[OrderDate]", Usage = "INEQUALITY" }
                },
                IncludeColumns = new List<IndexColumn>
                {
                    new() { Name = "[TotalDue]", Usage = "INCLUDE" }
                }
            };
        }
    }
}
