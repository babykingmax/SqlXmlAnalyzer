using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using FluentAssertions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;
using SqlXmlAnalyzer.Tests.Refactoring;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class ReviewWorkspaceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP21-" + Guid.NewGuid().ToString("N"));
    public ReviewWorkspaceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void SetABCommands_PublishUnmatchedListsAndClearThemWithComparison()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var vm = new MainViewModel();
            var ui = new PlanComparisonUiActionService(new PlanComparisonController(), new PlanComparisonTreeService(),
                new PlanComparisonTreeViewRenderer(), vm, new TabControl(), new TreeView(), new TreeView(), PlanComparisonMultiStatementTests.Ns);
            vm.PropertyChanged += ui.HandleViewModelPropertyChanged;
            vm.SetAsPlanACommand.CanExecute(null).Should().BeFalse(); vm.SetAsPlanBCommand.CanExecute("invalid").Should().BeFalse();
            var a = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 1) + PlanComparisonMultiStatementTests.Statement("SELECT 2", 2));
            var b = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 4) + PlanComparisonMultiStatementTests.Statement("SELECT 3", 3));
            var notifications = new List<string?>(); vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
            vm.SetAsPlanACommand.Execute(a); vm.SetAsPlanBCommand.Execute(b);
            vm.PlanA.Should().BeSameAs(a); vm.PlanB.Should().BeSameAs(b);
            vm.ComparisonSummary.Should().Contain("配对 1").And.Contain("A 未匹配 1").And.Contain("B 未匹配 1");
            vm.ComparisonUnmatchedA.Should().Contain("S2").And.Contain("B: 未匹配").And.Contain("SELECT 2").And.NotContain("SELECT 3");
            vm.ComparisonUnmatchedB.Should().Contain("S2").And.Contain("A: 未匹配").And.Contain("SELECT 3").And.NotContain("SELECT 2");
            vm.CostDeltaText.Should().Contain("N/A");
            vm.Comparison!.Statements.SelectMany(s => s.RootsB).SelectMany(n => n.RuntimeDeltas).Should().OnlyContain(m => m.PercentDelta == null);
            notifications.Should().Contain(nameof(vm.ComparisonUnmatchedA)).And.Contain(nameof(vm.ComparisonUnmatchedB));
            vm.SetComparisonPlans(b, a);
            vm.ComparisonUnmatchedA.Should().Contain("SELECT 3"); vm.ComparisonUnmatchedB.Should().Contain("SELECT 2");
            vm.ClearResults(); vm.ComparisonUnmatchedA.Should().Be("尚无比较结果"); vm.ComparisonUnmatchedB.Should().Be("尚无比较结果");
        });
    }

    [Fact]
    public void CaptureDifferencesRemainVisibleAndCannotBecomeImprovementClaims()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var vm = new MainViewModel();
            var ui = new PlanComparisonUiActionService(new PlanComparisonController(), new PlanComparisonTreeService(),
                new PlanComparisonTreeViewRenderer(), vm, new TabControl(), new TreeView(), new TreeView(), PlanComparisonMultiStatementTests.Ns);
            vm.PropertyChanged += ui.HandleViewModelPropertyChanged;
            var a = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 10));
            var b = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 1));
            b.Document.Root!.SetAttributeValue("Build", "17.0.1.1");
            vm.SetComparisonPlans(a, b);
            vm.ComparisonUnmatchedA.Should().Be("无未匹配项");
            vm.Comparison!.Statements.Single().Conditions.EstimatesComparable.Should().BeFalse();
            vm.Comparison.Statements.Single().RootsB.Single().CostPercentDelta.Should().BeNull();
            vm.ComparisonConditions.Should().Contain("17.0.1.1");
            vm.CostDeltaText.Should().NotContain("90%");
        });
    }

    [Fact]
    public void RewriteChecklist_ShowsExactStepDiffAndCannotExportAnUnselectedReview()
    {
        var result = RewriteProposalTests.Run(); var proposal = result.Review!.Proposals.Single();
        var vm = new RewriteReviewViewModel(RewriteProposalTests.Sql, result.Review);
        vm.CanExport.Should().BeFalse();
        vm.CopyCandidate(_ => throw new Exception("must not invoke"));
        var row = vm.Items.Single();
        row.Preconditions.Should().Contain(proposal.Preconditions[0]); row.Warnings.Should().Contain(proposal.Risks[0]);
        row.OriginalText.Should().Be(proposal.Diff.OriginalText); row.ReplacementText.Should().Be(proposal.Diff.ReplacementText);
        row.DiffContext.Should().Contain(proposal.BaseSqlHash).And.Contain("偏移");
        row.IsSelected = true;
        string? copied = null; vm.CopyCandidate(sql => copied = sql);
        copied.Should().Be(vm.PreviewSql).And.Be(result.OutputSql);
        vm.CanApply.Should().BeFalse(); vm.ApplyStatusText.Should().Be("尚未应用");
        row.IsSelected = false;
        vm.CanExport.Should().BeFalse(); vm.PreviewSql.Should().Be(RewriteProposalTests.Sql);
        vm.OutputStatus.Should().Contain("不会自动更新");
    }

    [Fact]
    public void SaveCandidateNew_PreservesExactSqlAndNeverOverwritesSourceOrPriorExport()
    {
        var result = RewriteProposalTests.Run(); var vm = new RewriteReviewViewModel(RewriteProposalTests.Sql, result.Review!);
        vm.Items[0].IsSelected = true;
        string original = Path.Combine(_directory, "source.sql"), candidate = Path.Combine(_directory, "candidate.sql");
        File.WriteAllText(original, vm.OriginalSql);
        vm.SaveCandidateNew(original);
        vm.Error.Should().NotBeEmpty(); File.ReadAllText(original).Should().Be(vm.OriginalSql);
        vm.SaveCandidateNew(candidate);
        vm.Error.Should().BeEmpty(); File.ReadAllText(candidate).Should().Be(vm.PreviewSql);
        byte[] first = File.ReadAllBytes(candidate);
        vm.SaveCandidateNew(candidate); File.ReadAllBytes(candidate).Should().Equal(first);
        vm.CanApply.Should().BeFalse(); vm.ApplyStatusText.Should().Be("尚未应用");
        Directory.GetFiles(_directory, ".tmp.review-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("candidate.txt")]
    [InlineData("candidate.sql:stream")]
    public void SaveCandidateNew_RejectsNonSqlDestinationWithoutDump(string path)
    {
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        new ReviewOutputService(reporter).SaveNew("SELECT 1;", path).Succeeded.Should().BeFalse();
        reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void BusyClipboard_IsRecoverableAndDoesNotAuthorizeApply()
    {
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        var result = new ReviewOutputService(reporter).Copy("SELECT 1;", _ => throw new ExternalException("clipboard busy"));
        result.Succeeded.Should().BeFalse(); result.Message.Should().Contain("重试");
        reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void FailedEncoding_DoesNotPublishPartialCandidateOrLeaveTemporaryFiles()
    {
        string destination = Path.Combine(_directory, "candidate.sql");
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        var result = new ReviewOutputService(reporter).SaveNew("SELECT N'\uD800';", destination);
        result.Succeeded.Should().BeFalse();
        File.Exists(destination).Should().BeFalse(); Directory.GetFiles(_directory).Should().BeEmpty();
        reporter.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("dbo")]
    [InlineData("sales")]
    public void IndexReview_CompilesExactObjectCustomNameKeyOrderAndEnvironmentOptions(string schema)
    {
        var suggestion = Suggestion(schema); var original = suggestion.CreateIndexStatement;
        var vm = new IndexSandboxViewModel(suggestion)
        { IndexName = "IX ] 名; DROP TABLE T;--", Online = false, SortInTempDb = false, MaxDop = "2", DataCompression = "ROW" };
        vm.MoveKeyColumnDownCommand.Execute(vm.KeyColumns[0]);
        vm.FullObject.Should().Be($"[server].[db].[{schema}].[Orders]");
        var parsed = IndexDdlIdentityTests.Parse<CreateIndexStatement>(vm.CreateIndexStatement);
        parsed.Name.Value.Should().Be(vm.IndexName);
        parsed.OnName.Identifiers.Select(i => i.Value).Should().Equal("db", schema, "Orders");
        parsed.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers.Single().Value).Should().Equal("B", "A");
        vm.CreateIndexStatement.Should().Contain("INCLUDE ([C])").And.Contain("ONLINE = OFF").And.Contain("SORT_IN_TEMPDB = OFF")
            .And.Contain("MAXDOP = 2").And.Contain("DATA_COMPRESSION = ROW");
        var rollback = IndexDdlIdentityTests.Parse<DropIndexStatement>(vm.RollbackStatement);
        ((DropIndexClause)rollback.DropIndexClauses.Single()).Index.Value.Should().Be(parsed.Name.Value);
        suggestion.CreateIndexStatement.Should().Be(original, "审核窗口应使用候选副本");
        vm.CostReductionSummary.Should().Contain("N/A"); vm.EnvironmentNotice.Should().Contain("不保证无阻塞");
        vm.CanExport.Should().BeTrue();
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("-1")]
    [InlineData("65")]
    [InlineData("1.5")]
    public void InvalidIndexOption_ClearsBothScriptsAndRecoversAfterCorrection(string value)
    {
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        var vm = new IndexSandboxViewModel(Suggestion(), unexpectedErrors: reporter) { MaxDop = value };
        vm.CanExport.Should().BeFalse(); vm.CreateIndexStatement.Should().BeEmpty(); vm.RollbackStatement.Should().BeEmpty(); vm.Error.Should().NotBeEmpty();
        vm.CopyScript(_ => throw new Exception("must not invoke"));
        vm.MaxDop = ""; vm.CanExport.Should().BeTrue(); vm.Error.Should().BeEmpty();
        vm.IndexName = new string('X', 129); vm.CanExport.Should().BeFalse();
        vm.IndexName = "IX_test"; vm.CompiledIndexName.Should().Be("IX_test"); vm.CanExport.Should().BeTrue();
        reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void RemovingLastKey_DisablesCopyAndReaddingItRestoresAuditableScript()
    {
        var vm = new IndexSandboxViewModel(Suggestion());
        foreach (var column in vm.KeyColumns.ToArray()) vm.RemoveKeyColumnCommand.Execute(column);
        vm.CanExport.Should().BeFalse(); vm.CreateIndexStatement.Should().BeEmpty(); vm.RollbackStatement.Should().BeEmpty();
        vm.Error.Should().Contain("键列");
        vm.AddKeyColumnCommand.Execute("[A]");
        string? copied = null; vm.CopyScript(sql => copied = sql);
        copied.Should().Be(vm.CreateIndexStatement); vm.OutputStatus.Should().Contain("本次操作仅复制文本").And.Contain("未脱敏");
    }

    internal static MissingIndexSuggestion Suggestion(string schema = "dbo") => new()
    {
        Server = "server", Database = "db", Schema = schema, Table = "Orders",
        KeyColumns = [new() { Name = "A", Usage = "EQUALITY" }, new() { Name = "B", Usage = "INEQUALITY" }],
        IncludeColumns = [new() { Name = "C", Usage = "INCLUDE" }],
        Source = IndexSuggestionSource.CapturedMissingIndex, CapturedImpact = 72
    };

    public void Dispose()
    {
        if (!Path.GetFullPath(_directory).StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP21-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(_directory, true);
    }
}
