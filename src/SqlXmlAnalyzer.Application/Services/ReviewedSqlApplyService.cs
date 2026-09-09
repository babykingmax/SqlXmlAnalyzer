using System.Text.Json;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Refactoring;

namespace SqlXmlAnalyzer.Application.Services;

public sealed class ReviewedSqlApplyService
{
    private readonly ISqlWritebackService _writeback;
    private readonly ISqlSemanticRunner _runner;
    private readonly IUnexpectedErrorReporter _unexpectedErrors;
    private readonly RewriteProposalService _proposals;

    public ReviewedSqlApplyService(ISqlWritebackService writeback, ISqlSemanticRunner runner, IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        _writeback = writeback; _runner = runner;
        _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
        _proposals = new(_unexpectedErrors);
    }

    public async Task<PrepareSqlRewriteResult> PrepareAsync(string path, string original, RewriteReview review,
        SqlSemanticSuite suite, CancellationToken token = default)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            SqlSemanticSandboxPolicy.ParseBatches(original);
            var current = Rebuild(original, review);
            if (!current.Proposals.Any(p => p.IsSelected) || current.PreviewSql == original)
                throw new InvalidDataException("没有选择有效的 SQL 改写，无需应用。");
            var source = _writeback.ReadSnapshot(path, SqlSemanticSandboxPolicy.MaxSqlCharacters, token);
            if (!string.Equals(source.Text, original, StringComparison.Ordinal))
                throw new InvalidDataException("源文件与审核原文不一致，请重新读取并生成提案。");
            SqlSemanticSandboxPolicy.Validate(suite);
            Logger.Debug("ReviewedApply.Prepare: 开始验证所选组合。");
            var validation = await _runner.ValidateAsync(original, current.PreviewSql, suite, token);
            token.ThrowIfCancellationRequested();
            if (!validation.CanPrepareApply || validation.SourceHash != SqlTextHash.Compute(original) ||
                validation.CandidateHash != SqlTextHash.Compute(current.PreviewSql) || validation.SuiteHash != suite.Fingerprint ||
                !validation.Cases.Select(c => c.Name).SequenceEqual(suite.Scenarios.Select(s => s.Name)) ||
                validation.CompatibilityLevel != suite.CompatibilityLevel || validation.Collation != suite.Collation ||
                string.IsNullOrWhiteSpace(validation.ServerVersion) || string.IsNullOrWhiteSpace(validation.SessionSettings))
            {
                Logger.Warning("ReviewedApply.NotValidated: 本次验证未满足应用前提。");
                return new(null, validation, "验证未完成、场景不匹配或证据与所选内容不一致，禁止应用。");
            }
            if (_writeback.ReadSnapshot(path, SqlSemanticSandboxPolicy.MaxSqlCharacters, token).Sha256 != source.Sha256)
                throw new InvalidDataException("源文件在数据库验证期间发生变化，请重新读取和审核。");
            Logger.Debug("ReviewedApply.Prepared: 已生成本轮审核应用凭据。");
            return new(new(source, Fingerprint(current), current.PreviewSql, validation), validation, null);
        }
        catch (Exception exception)
        {
            var error = Describe(exception, "ReviewedApply.Prepare");
            return new(null, null, error.Message, error.Diagnostic);
        }
    }

    public ApplySqlRewriteResult Apply(PreparedSqlRewrite prepared, RewriteReview currentReview, bool reviewedScenarios,
        CancellationToken token = default)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (prepared == null || !reviewedScenarios || !prepared.CanApply)
                throw new InvalidDataException("需要有效的实时验证及对这些场景限制的明确审核；历史报告或已使用凭据不能应用。");
            var review = Rebuild(prepared.Source.Text, currentReview);
            if (Fingerprint(review) != prepared.ReviewHash || review.PreviewSql != prepared.Candidate)
                throw new InvalidDataException("选择、规则版本或提案内容已变化，请重新验证。");
            if (_writeback.ReadSnapshot(prepared.Source.Path, SqlSemanticSandboxPolicy.MaxSqlCharacters, token).Sha256 != prepared.Source.Sha256)
                throw new InvalidDataException("源文件在审核后发生变化，禁止写回。");
            if (Interlocked.CompareExchange(ref prepared.Consumed, 1, 0) != 0)
                throw new InvalidDataException("本轮验证凭据已被使用，请重新验证。");
            Logger.Debug("ReviewedApply.Commit: 使用已验证的所选预览进行安全写回。");
            var writeback = _writeback.WriteBack(prepared.Source, prepared.Candidate, token);
            // Preserve confirmed writes and uncertain outcomes even on a failed commit acknowledgement.
            return new(writeback.IsSuccess, writeback, writeback.ErrorMessage);
        }
        catch (Exception exception)
        {
            var error = Describe(exception, "ReviewedApply.Commit");
            return new(false, null, error.Message, error.Diagnostic);
        }
    }

    private RewriteReview Rebuild(string source, RewriteReview review)
    {
        if (review == null) throw new InvalidDataException("缺少审核提案。");
        var result = _proposals.Review(source, review.Proposals, review.Proposals.Where(p => p.IsSelected).Select(p => p.Id), review.Warnings);
        if (review.SourceHash != result.SourceHash || review.PreviewSql != result.PreviewSql)
            throw new InvalidDataException("审核预览与源/选择不一致。");
        return result;
    }

    private static string Fingerprint(RewriteReview review) => SqlTextHash.Compute(JsonSerializer.Serialize(new
    { review.SourceHash, review.Proposals, PreviewHash = SqlTextHash.Compute(review.PreviewSql) }));

    private (string Message, UnexpectedErrorReport? Diagnostic) Describe(Exception exception, string operation)
    {
        if (ExceptionPolicy.IsExpected(exception)) return (ExceptionPolicy.Describe(exception, operation, _unexpectedErrors), null);
        var diagnostic = ExceptionPolicy.Capture(exception, operation, _unexpectedErrors);
        return ("未知审核应用错误；" + diagnostic.Summary, diagnostic);
    }
}
