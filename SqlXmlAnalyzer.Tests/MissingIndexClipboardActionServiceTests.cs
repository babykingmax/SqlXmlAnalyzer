using System.Collections.Generic;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class MissingIndexClipboardActionServiceTests
    {
        [Fact]
        public void BuildCreateScript_WhenDdlIsProvided_ReturnsReadyResult()
        {
            var service = new MissingIndexClipboardActionService();

            MissingIndexClipboardActionResult result =
                service.BuildCreateScript("CREATE INDEX IX_Test ON dbo.Orders(Id);");

            result.Status.Should().Be(MissingIndexClipboardActionStatus.Ready);
            result.Text.Should().Be("CREATE INDEX IX_Test ON dbo.Orders(Id);");
            result.SuccessMessage.Should().Be("CREATE INDEX DDL copied to clipboard.");
        }

        [Fact]
        public void BuildCreateScript_WhenDdlIsEmpty_ReturnsMissingContent()
        {
            var service = new MissingIndexClipboardActionService();

            MissingIndexClipboardActionResult result = service.BuildCreateScript("");

            result.Status.Should().Be(MissingIndexClipboardActionStatus.MissingContent);
            result.Text.Should().BeEmpty();
            result.SuccessMessage.Should().BeEmpty();
        }

        [Fact]
        public void BuildRollbackScript_WhenDdlIsProvided_ReturnsReadyResult()
        {
            var service = new MissingIndexClipboardActionService();

            MissingIndexClipboardActionResult result =
                service.BuildRollbackScript("DROP INDEX IX_Test ON dbo.Orders;");

            result.Status.Should().Be(MissingIndexClipboardActionStatus.Ready);
            result.Text.Should().Be("DROP INDEX IX_Test ON dbo.Orders;");
            result.SuccessMessage.Should().Be("DROP INDEX rollback DDL copied to clipboard.");
        }

        [Fact]
        public void BuildDeploymentBundle_WhenSuggestionIsProvided_ReturnsBundle()
        {
            var service = new MissingIndexClipboardActionService();

            MissingIndexClipboardActionResult result =
                service.BuildDeploymentBundle(CreateSuggestion());

            result.Status.Should().Be(MissingIndexClipboardActionStatus.Ready);
            result.Text.Should().Contain("SQL Server Missing Index Deployment Bundle");
            result.Text.Should().Contain("CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId]");
            result.Text.Should().Contain("DROP INDEX [IX_Orders_CustomerId] ON [sales].[Orders];");
            result.SuccessMessage.Should().Be("Deployment bundle copied to clipboard.");
        }

        [Fact]
        public void BuildDeploymentBundle_WhenSuggestionIsMissing_ReturnsMissingContent()
        {
            var service = new MissingIndexClipboardActionService();

            MissingIndexClipboardActionResult result = service.BuildDeploymentBundle(null);

            result.Status.Should().Be(MissingIndexClipboardActionStatus.MissingContent);
            result.Text.Should().BeEmpty();
            result.SuccessMessage.Should().BeEmpty();
        }

        private static MissingIndexSuggestion CreateSuggestion()
        {
            return new MissingIndexSuggestion
            {
                Schema = "[sales]",
                Table = "[Orders]",
                Impact = 82.3,
                Score = 88,
                KeyColumns = new List<IndexColumn>
                {
                    new() { Name = "[CustomerId]", Usage = "EQUALITY" }
                },
                IncludeColumns = new List<IndexColumn>
                {
                    new() { Name = "[TotalDue]", Usage = "INCLUDE" }
                }
            };
        }
    }
}
