using System;
using System.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Refactoring.Rules;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class SqlQuickFixServiceTests
    {
        [Fact]
        public void TryRewriteSelectedSubquery_WhenRewriteIsAvailable_ReturnsRewrittenSql()
        {
            const string sql =
                "SELECT c.Name, (SELECT COUNT(o.Id) FROM Orders o WHERE o.CustomerId = c.Id) AS OrderCount FROM Customers c";
            var subquery = ScalarSubqueryToJoinRule.GetRewriteableSubqueries(sql).Single();
            var service = new SqlQuickFixService();

            SqlQuickFixResult result = service.TryRewriteSelectedSubquery(
                sql,
                subquery.StartOffset,
                subquery.FragmentLength);

            result.IsAvailable.Should().BeTrue();
            result.RewrittenSql.Should().Contain("LEFT OUTER JOIN");
            result.StatementPreview.Should().Be(result.RewrittenSql);
            result.FailureMessage.Should().BeEmpty();
        }

        [Fact]
        public void TryRewriteSelectedSubquery_WhenRewriteIsUnavailable_ReturnsFailureMessage()
        {
            const string sql =
                "SELECT c.Name, (SELECT COUNT(o.Id) FROM Orders o WHERE o.CustomerId = c.Id) AS OrderCount FROM Customers c";
            var service = new SqlQuickFixService();

            SqlQuickFixResult result = service.TryRewriteSelectedSubquery(
                sql,
                subqueryStartOffset: 0,
                subqueryLength: 1);

            result.IsAvailable.Should().BeFalse();
            result.RewrittenSql.Should().Be(sql);
            result.StatementPreview.Should().Be(sql);
            result.FailureMessage.Should().Be(
                SqlQuickFixService.SelectedSubqueryRewriteUnavailableMessage);
        }

        [Fact]
        public void CreateStatementPreview_WhenSqlIsLong_TruncatesToPreviewLength()
        {
            var service = new SqlQuickFixService();
            string sql = new('A', 805);

            string preview = service.CreateStatementPreview(sql);

            preview.Should().HaveLength(803);
            preview.Should().EndWith("...");
        }

        [Fact]
        public void CreateStatementPreview_WhenSqlIsNull_Throws()
        {
            var service = new SqlQuickFixService();

            Action act = () => service.CreateStatementPreview(null!);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
