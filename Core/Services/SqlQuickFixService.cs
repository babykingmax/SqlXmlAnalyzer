using System;
using SqlXmlAnalyzer.Refactoring.Rules;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record SqlQuickFixResult(
        bool IsAvailable,
        string RewrittenSql,
        string StatementPreview,
        string FailureMessage);

    public sealed class SqlQuickFixService
    {
        private const int StatementPreviewLength = 800;
        public const string SelectedSubqueryRewriteUnavailableMessage =
            "无法安全地重写所选标量子查询。SQL 未被修改。";

        public SqlQuickFixResult TryRewriteSelectedSubquery(
            string originalSql,
            int subqueryStartOffset,
            int subqueryLength)
        {
            ArgumentNullException.ThrowIfNull(originalSql);

            if (!ScalarSubqueryToJoinRule.TryRewriteSelectedSubquery(
                    originalSql,
                    subqueryStartOffset,
                    subqueryLength,
                    out string rewrittenSql))
            {
                return new SqlQuickFixResult(
                    IsAvailable: false,
                    originalSql,
                    CreateStatementPreview(originalSql),
                    SelectedSubqueryRewriteUnavailableMessage);
            }

            return new SqlQuickFixResult(
                IsAvailable: true,
                rewrittenSql,
                CreateStatementPreview(rewrittenSql),
                string.Empty);
        }

        public string CreateStatementPreview(string sql)
        {
            ArgumentNullException.ThrowIfNull(sql);

            if (sql.Length <= StatementPreviewLength)
            {
                return sql;
            }

            return sql.Substring(0, StatementPreviewLength) + "...";
        }
    }
}
