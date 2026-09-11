using System;
using SqlXmlAnalyzer.Refactoring.Rules;
using SqlXmlAnalyzer.Refactoring;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record SqlQuickFixResult(
        bool IsAvailable,
        string RewrittenSql,
        string StatementPreview,
        string FailureMessage)
    {
        public RewriteReview? Review { get; init; }
    }

    public sealed class SqlQuickFixService
    {
        private readonly IUnexpectedErrorReporter _unexpectedErrors;
        public SqlQuickFixService(IUnexpectedErrorReporter? unexpectedErrors = null) =>
            _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
        private const int StatementPreviewLength = 800;
        public const string SelectedSubqueryRewriteUnavailableMessage =
            "无法安全地重写所选标量子查询。SQL 未被修改。";

        public SqlQuickFixResult TryRewriteSelectedSubquery(
            string originalSql,
            int subqueryStartOffset,
            int subqueryLength)
        {
            try
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

                var service = new RewriteProposalService(_unexpectedErrors);
                var proposal = service.Propose(originalSql, originalSql, rewrittenSql,
                    "REF_RULE_107_SCALAR_SUBQUERY_JOIN",
                    typeof(ScalarSubqueryToJoinRule).Assembly.GetName().Version?.ToString() ?? "unversioned",
                    "所选标量子查询的 JOIN 候选，等价性未证明。", [], new RefactorContext(originalSql));
                if (!proposal.Validation.IsValid)
                    return new(false, originalSql, CreateStatementPreview(originalSql), "候选验证失败，保留原文。");
                return new SqlQuickFixResult(
                    IsAvailable: true,
                    rewrittenSql,
                    CreateStatementPreview(rewrittenSql),
                    string.Empty) { Review = service.Review(originalSql, [proposal]) };
            }
            catch (Exception exception)
            {
                string detail = ExceptionPolicy.Describe(exception, "SqlQuickFix.Propose", _unexpectedErrors);
                return new(false, originalSql, originalSql, "生成提案失败，保留原文：" + detail);
            }
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
