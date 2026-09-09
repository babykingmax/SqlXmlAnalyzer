using System.Collections.Immutable;
using System.IO;
using System.Text;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Refactoring;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class ReviewedSqlApplyServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP19-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_directory, "source.sql");
    private const string Sql = "SELECT 1 AS Id;";
    private readonly SqlSemanticSuite _suite = new([new("case", "")]);
    private readonly Runner _runner = new();
    private readonly Files _files = new();
    private readonly RewriteReview _review;
    private readonly ReviewedSqlApplyService _service;

    public ReviewedSqlApplyServiceTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Source, Sql, new UTF8Encoding(true));
        var proposals = new RewriteProposalService();
        var item = proposals.Propose(Sql, Sql, "SELECT 2 AS Id;", "TEST", "1", "test", [], new(Sql));
        _review = proposals.Review(Sql, [item], [item.Id]);
        _service = new(new SqlWritebackService(_files), _runner);
    }

    internal static SqlSemanticReport Passed(string source, string candidate, SqlSemanticSuite suite)
    {
        var execution = new SqlExecutionObservation([], [], 0);
        var observation = new SqlSemanticObservation(execution, execution, execution, 0, 0, 0);
        return new(SemanticValidationStatus.PassedForScenarios, SqlTextHash.Compute(source), SqlTextHash.Compute(candidate), suite.Fingerprint,
            "17.0.synthetic", suite.CompatibilityLevel, suite.Collation, "test settings",
            suite.Scenarios.Select(s => new SqlSemanticCaseResult(s.Name, true, true, observation, observation)).ToImmutableArray(), [], [], []);
    }

    [Fact]
    public async Task Apply_WritesExactSelectedSqlPreservesEncodingAndVerifiedBackup()
    {
        byte[] original = File.ReadAllBytes(Source);
        var prepared = await _service.PrepareAsync(Source, Sql, _review, _suite);
        prepared.CanApply.Should().BeTrue();
        File.ReadAllBytes(Source).Should().Equal(original);
        var applied = _service.Apply(prepared.Prepared!, _review, true);
        applied.IsSuccess.Should().BeTrue();
        applied.SourceWritten.Should().BeTrue();
        File.ReadAllText(Source).Should().Be(_review.PreviewSql);
        File.ReadAllBytes(Source).Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
        File.ReadAllBytes(applied.Writeback!.BackupPath!).Should().Equal(original);
        prepared.Prepared!.CanApply.Should().BeFalse();
        _service.Apply(prepared.Prepared!, _review, true).IsSuccess.Should().BeFalse();
        _files.Replaces.Should().Be(1);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("source")]
    [InlineData("candidate")]
    [InlineData("suite")]
    [InlineData("missing-case")]
    [InlineData("sql-error")]
    [InlineData("cleanup")]
    public async Task Prepare_InvalidOrIncompleteEvidenceCannotEnableApply(string kind)
    {
        _runner.Modify = report => kind switch
        {
            "status" => report with { Status = SemanticValidationStatus.Different },
            "source" => report with { SourceHash = "wrong" },
            "candidate" => report with { CandidateHash = "wrong" },
            "suite" => report with { SuiteHash = "wrong" },
            "missing-case" => report with { Cases = [] },
            "sql-error" => report with { Cases = [report.Cases[0] with { CompletedWithoutErrors = false }] },
            _ => report with { CleanupFailures = ["not cleaned"] }
        };
        var prepared = await _service.PrepareAsync(Source, Sql, _review, _suite);
        prepared.CanApply.Should().BeFalse();
        _files.Replaces.Should().Be(0);
        File.ReadAllText(Source).Should().Be(Sql);
    }

    [Theory]
    [InlineData("DELETE FROM dbo.T;")]
    [InlineData("SELECT 1 AS Id INTO #probe;")]
    [InlineData("ROLLBACK;")]
    public async Task Prepare_MutatingObserverCannotReachDatabaseOrIssueCredential(string observer)
    {
        bool called = false;
        _runner.Modify = report => { called = true; return report; };
        var result = await _service.PrepareAsync(Source, Sql, _review, new([new("case", "", observer)]));
        called.Should().BeFalse();
        result.CanApply.Should().BeFalse();
        result.Error.Should().Contain("ObserverReadOnly");
        result.Diagnostic.Should().BeNull();
        _files.Replaces.Should().Be(0);
        File.ReadAllText(Source).Should().Be(Sql);
        Directory.GetFiles(_directory, "*.bak").Should().BeEmpty();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("selection")]
    [InlineData("version")]
    [InlineData("review")]
    [InlineData("cancel")]
    public async Task Apply_StaleUnreviewedOrCanceledRequestDoesNotWrite(string kind)
    {
        var prepared = await _service.PrepareAsync(Source, Sql, _review, _suite);
        var current = _review;
        if (kind == "source") File.AppendAllText(Source, " -- external");
        if (kind == "selection") current = new RewriteProposalService().Review(Sql, _review.Proposals);
        if (kind == "version") current = _review with { Proposals = [_review.Proposals[0] with { RuleVersion = "2" }] };
        byte[] expected = File.ReadAllBytes(Source);
        var applied = _service.Apply(prepared.Prepared!, current, kind != "review", new CancellationToken(kind == "cancel"));
        applied.IsSuccess.Should().BeFalse();
        applied.SourceWritten.Should().BeFalse();
        _files.Replaces.Should().Be(0);
        File.ReadAllBytes(Source).Should().Equal(expected);
    }

    [Fact]
    public async Task Prepare_SourceChangesDuringDatabaseValidation_IsRejected()
    {
        _runner.Modify = result => { File.AppendAllText(Source, " -- concurrent"); return result; };
        (await _service.PrepareAsync(Source, Sql, _review, _suite)).CanApply.Should().BeFalse();
        _files.Replaces.Should().Be(0);
    }

    [Theory]
    [InlineData("before-prepare")]
    [InlineData("during-validation")]
    [InlineData("before-apply")]
    public async Task Source_GrowingPastBudgetCannotIssueOrUseCredential(string when)
    {
        void Grow() => File.WriteAllText(Source, new string('x', SqlSemanticSandboxPolicy.MaxSqlCharacters + 1));
        if (when == "before-prepare") Grow();
        if (when == "during-validation") _runner.Modify = report => { Grow(); return report; };
        var prepared = await _service.PrepareAsync(Source, Sql, _review, _suite);
        if (when == "before-apply")
        {
            prepared.CanApply.Should().BeTrue();
            Grow();
            var result = _service.Apply(prepared.Prepared!, _review, true);
            result.SourceWritten.Should().BeFalse();
            result.Error.Should().Contain("SqlInputBudget:");
        }
        else
        {
            prepared.CanApply.Should().BeFalse();
            prepared.Error.Should().Contain("SqlInputBudget:");
        }
        _files.Replaces.Should().Be(0);
        Directory.GetFiles(_directory, "*.bak").Should().BeEmpty();
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("concurrent")]
    [InlineData("replace-after-commit")]
    public async Task Apply_WritebackFaultPreservesActualCommitStateAndRecovery(string failure)
    {
        byte[] original = File.ReadAllBytes(Source);
        var prepared = await _service.PrepareAsync(Source, Sql, _review, _suite);
        _files.Failure = failure;
        var applied = _service.Apply(prepared.Prepared!, _review, true);
        applied.IsSuccess.Should().BeFalse();
        if (failure == "replace-after-commit")
        {
            applied.SourceWritten.Should().BeTrue();
            File.ReadAllText(Source).Should().Be(_review.PreviewSql);
            applied.Writeback!.BackupVerified.Should().BeTrue();
        }
        else if (failure == "backup") File.ReadAllBytes(Source).Should().Equal(original);
        else File.ReadAllText(Source).Should().Be(Sql + " -- concurrent");
        if (failure != "backup") File.ReadAllBytes(applied.Writeback!.BackupPath!).Should().Equal(original);
    }

    private sealed class Runner : ISqlSemanticRunner
    {
        public Func<SqlSemanticReport, SqlSemanticReport>? Modify;
        public Task<SqlSemanticReport> ValidateAsync(string original, string candidate, SqlSemanticSuite suite, CancellationToken token = default)
        {
            var report = Passed(original, candidate, suite);
            return Task.FromResult(Modify?.Invoke(report) ?? report);
        }
    }
    private sealed class Files : ISqlWritebackFileSystem
    {
        private readonly PhysicalSqlWritebackFileSystem _inner = new();
        private string? _source;
        public string? Failure;
        public int Replaces;
        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
        public byte[] ReadAllBytes(string path, int maxBytes, CancellationToken cancellationToken) =>
            _inner.ReadAllBytes(path, maxBytes, cancellationToken);
        public Stream CreateNew(string path, string sourcePath)
        {
            _source = sourcePath;
            if (Failure == "backup" && path.EndsWith(".bak")) throw new IOException("test backup unavailable");
            return _inner.CreateNew(path, sourcePath);
        }
        public void FlushToDisk(Stream stream)
        {
            _inner.FlushToDisk(stream);
            if (Failure == "concurrent") { Failure = null; File.AppendAllText(_source!, " -- concurrent"); }
        }
        public void Replace(string temporaryPath, string sourcePath)
        {
            Replaces++; _inner.Replace(temporaryPath, sourcePath);
            if (Failure == "replace-after-commit") throw new IOException("test acknowledgement lost");
        }
        public void Delete(string path) => _inner.Delete(path);
    }
    public void Dispose()
    {
        string full = Path.GetFullPath(_directory);
        if (!full.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP19-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(full, true);
    }
}
