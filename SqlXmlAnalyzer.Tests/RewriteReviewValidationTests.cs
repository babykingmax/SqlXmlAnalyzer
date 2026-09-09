using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Tests.Application;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Refactoring;

namespace SqlXmlAnalyzer.Tests;

public sealed class RewriteReviewValidationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP19View-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_directory, "source.sql");
    private const string Sql = "SELECT 1 AS Id;";
    private readonly SqlSemanticSuite _suite = new([new("test", "")]);
    public RewriteReviewValidationTests() { Directory.CreateDirectory(_directory); File.WriteAllText(Source, Sql); }

    private RewriteReviewViewModel Model(Runner runner, ISqlWritebackService? files = null)
    {
        var service = new RewriteProposalService();
        var proposal = service.Propose(Sql, Sql, "SELECT 2 AS Id;", "TEST", "1", "test", [], new(Sql));
        var review = service.Review(Sql, [proposal], [proposal.Id]);
        return new(Sql, review, applyService: new(files ?? new SqlWritebackService(new PhysicalSqlWritebackFileSystem()), runner));
    }

    [Fact]
    public async Task ViewModel_RequiresValidationAndScenarioAcknowledgementBeforeApply()
    {
        var model = Model(new());
        model.CanApply.Should().BeFalse();
        model.CanValidate.Should().BeTrue();
        await model.ValidateAsync(Source, _suite);
        model.CanApply.Should().BeFalse();
        model.ReviewedScenarios = true;
        model.CanApply.Should().BeTrue();
        model.Apply()!.SourceWritten.Should().BeTrue();
        File.ReadAllText(Source).Should().Be(model.PreviewSql);
        model.CanApply.Should().BeFalse();
        model.CanValidate.Should().BeFalse();
        model.ValidationDetails.Should().Contain("已写回").And.Contain("备份");
        model.Details.Should().NotContain("尚未应用");
        model.ApplyStatusText.Should().Be("已写回");
        model.PreviewTitle.Should().Contain("已写回").And.NotContain("尚未应用");
        model.ApplySummary.Should().Contain(Source).And.Contain("备份");
    }

    [Theory]
    [InlineData(true, false, "已写回")]
    [InlineData(false, true, "提交结果未知")]
    [InlineData(false, false, "未写回")]
    public async Task ViewModel_FailedApplyShowsActualCommitStateProminently(bool written, bool unknown, string expected)
    {
        var model = Model(new(), new OutcomeFiles(written, unknown));
        var changed = new HashSet<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await model.ValidateAsync(Source, _suite);
        model.ReviewedScenarios = true;
        model.Apply()!.IsSuccess.Should().BeFalse();
        model.ApplyStatusText.Should().Contain(expected);
        model.PreviewTitle.Should().Contain(expected).And.NotContain("尚未应用");
        model.Details.Should().Contain(expected).And.NotContain("尚未应用");
        model.ApplySummary.Should().Contain("backup.sql");
        changed.Should().Contain(nameof(model.ApplyStatusText)).And.Contain(nameof(model.PreviewTitle)).And.Contain(nameof(model.Details));
        model.CanApply.Should().BeFalse();
        model.CanSelect.Should().Be(!written && !unknown);
        if (written || unknown)
        {
            model.Items[0].IsSelected = false;
            model.ApplyStatusText.Should().Contain(expected);
            model.Items[0].IsSelected.Should().BeTrue();
        }
    }

    private sealed class OutcomeFiles(bool written, bool unknown) : ISqlWritebackService
    {
        private readonly SqlWritebackService _files = new(new PhysicalSqlWritebackFileSystem());
        public SqlFileSnapshot ReadSnapshot(string path, CancellationToken cancellationToken = default) => _files.ReadSnapshot(path, cancellationToken);
        public SqlFileSnapshot ReadSnapshot(string path, int maxCharacters, CancellationToken cancellationToken = default) =>
            _files.ReadSnapshot(path, maxCharacters, cancellationToken);
        public SqlWritebackResult WriteBack(SqlFileSnapshot snapshot, string sql, CancellationToken cancellationToken = default) =>
            new(false, written, unknown, false, SqlWritebackStage.Replace, true, "backup.sql", null,
                snapshot.Sha256, null, "synthetic commit acknowledgement failure", null, []);
    }

    [Fact]
    public async Task ViewModel_SelectionChangeDuringValidationDiscardsCredential()
    {
        var runner = new Runner { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var model = Model(runner);
        var pending = model.ValidateAsync(Source, _suite);
        model.CanSelect.Should().BeFalse();
        model.ReviewedScenarios = true;
        model.ReviewedScenarios.Should().BeFalse("the results have not arrived for review yet");
        model.Items[0].IsSelected = false;
        runner.Gate.SetResult();
        await pending;
        model.ReviewedScenarios = true;
        model.CanApply.Should().BeFalse();
        model.Error.Should().Contain("选择已变化");
        model.PreviewSql.Should().Be(Sql);
        File.ReadAllText(Source).Should().Be(Sql);
    }

    [Fact]
    public async Task ViewModel_ChangingSelectionAfterValidationRequiresFreshDatabaseRun()
    {
        var model = Model(new());
        await model.ValidateAsync(Source, _suite);
        model.ReviewedScenarios = true;
        model.CanApply.Should().BeTrue();
        model.Items[0].IsSelected = false;
        model.Items[0].IsSelected = true;
        model.CanApply.Should().BeFalse();
        model.ReviewedScenarios.Should().BeFalse();
    }

    private sealed class Runner : ISqlSemanticRunner
    {
        public TaskCompletionSource? Gate;
        public async Task<SqlSemanticReport> ValidateAsync(string original, string candidate, SqlSemanticSuite suite, CancellationToken token = default)
        {
            if (Gate != null) await Gate.Task;
            return ReviewedSqlApplyServiceTests.Passed(original, candidate, suite);
        }
    }
    public void Dispose()
    {
        string full = Path.GetFullPath(_directory);
        if (!full.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP19View-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(full, true);
    }
}
