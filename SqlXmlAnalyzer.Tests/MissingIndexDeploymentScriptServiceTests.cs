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
            bundle.Should().Contain(" * SQL Server Impact: 89.12%");
            bundle.Should().Contain(" * Score:  92/100");
            bundle.Should().Contain("-- === 1. DEPLOYMENT DDL (CREATE INDEX) ===");
            bundle.Should().Contain("BEGIN TRANSACTION;");
            bundle.Should().MatchRegex(@"CREATE NONCLUSTERED INDEX \[IX_Orders_[0-9A-F]{64}\]");
            bundle.Should().Contain("-- === 2. ROLLBACK DDL (DROP INDEX) ===");
            bundle.Should().MatchRegex(@"DROP INDEX \[IX_Orders_[0-9A-F]{64}\] ON \[sales\].\[Orders\];");
        }

        [Fact]
        public void BuildDeploymentBundle_WhenSchemaIsEmpty_RejectsUnresolvedTarget()
        {
            var service = new MissingIndexDeploymentScriptService();
            MissingIndexSuggestion suggestion = CreateSuggestion();
            suggestion.Schema = string.Empty;

            Action action = () => service.BuildDeploymentBundle(suggestion);
            action.Should().Throw<System.IO.InvalidDataException>().WithMessage("INDEX_TARGET_UNRESOLVED*");
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
                CapturedImpact = 89.123,
                Source = IndexSuggestionSource.CapturedMissingIndex,
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
