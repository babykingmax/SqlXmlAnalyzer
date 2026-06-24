using FluentAssertions;
using SqlXmlAnalyzer.Refactoring.Rules;

namespace SqlXmlAnalyzer.Tests.Refactoring
{
    public class ScalarSubqueryQuickFixTests
    {
        [Fact]
        public void TryRewriteSelectedSubquery_WithTwoCandidates_RewritesOnlySelectedCandidate()
        {
            const string sql = "SELECT c.Name, (SELECT COUNT(o.Id) FROM Orders o WHERE o.CustomerId = c.Id) AS OrderCount, (SELECT SUM(p.Amount) FROM Payments p WHERE p.CustomerId = c.Id) AS TotalPaid FROM Customers c";
            var subqueries = ScalarSubqueryToJoinRule.GetRewriteableSubqueries(sql);

            subqueries.Should().HaveCount(2);

            var applied = ScalarSubqueryToJoinRule.TryRewriteSelectedSubquery(
                sql,
                subqueries[0].StartOffset,
                subqueries[0].FragmentLength,
                out var rewrittenSql);

            applied.Should().BeTrue();
            rewrittenSql.Should().Contain("LEFT OUTER JOIN");
            rewrittenSql.Should().Contain("ISNULL(t_sub_0.agg_0, 0) AS OrderCount");
            rewrittenSql.Should().Contain("SELECT SUM(p.Amount)");
            rewrittenSql.Should().NotContain("t_sub_1");
        }

        [Fact]
        public void TryRewriteSelectedSubquery_WithUnknownRange_DoesNotChangeSql()
        {
            const string sql = "SELECT c.Name, (SELECT COUNT(o.Id) FROM Orders o WHERE o.CustomerId = c.Id) AS OrderCount FROM Customers c";

            var applied = ScalarSubqueryToJoinRule.TryRewriteSelectedSubquery(sql, 0, 1, out var rewrittenSql);

            applied.Should().BeFalse();
            rewrittenSql.Should().Be(sql);
        }
    }
}
