using System.IO;
using System.Text;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;
using static SqlXmlAnalyzer.Tests.RuleConfigurationDocumentTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class RuleConfigurationViewModelTests
{
    [Fact]
    public async Task RowsFilterPreviewReset_PreserveUnknownConfiguration()
    {
        using var model = new RuleConfigurationViewModel(new(RuleConfigurationDocument.Defaults), store: new Store());
        await model.InitializeAsync();
        model.Rows.Should().HaveCount(34);
        model.Rows.Should().OnlyContain(row => row.Description.Length > 0 && row.Scope.Length > 0 && row.Category.Length > 0);
        model.Filter = "estimate_mismatch"; model.VisibleRows.Should().ContainSingle();
        model.VisibleRows[0].Enabled = true;
        model.ChangeCount.Should().Be(1); model.Preview.Should().Contain(Id).And.Contain("False → True");
        model.OnlyChanged = true; model.VisibleRows.Should().ContainSingle();
        model.RestoreDefaults(); model.Draft!.Fingerprint.Should().Be(RuleConfigurationDocument.Defaults.Fingerprint);
        model.Draft.UnknownRules.Should().ContainSingle(); model.Notices.Should().Contain("FUTURE_RULE");
        model.VisibleRows[0].Enabled = false; model.VisibleRows[0].SeveritySelection = "Info";
        model.ChangeCount.Should().Be(0); model.VisibleRows.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAndSave_AreIndependentAndSnapshotDoesNotMutate()
    {
        var store = new Store(); var session = new RuleConfigurationSession(RuleConfigurationDocument.Defaults);
        var initial = session.Capture(); var changes = new List<bool>();
        using var model = new RuleConfigurationViewModel(session, changes.Add, store: store);
        await model.InitializeAsync(); model.Apply();
        changes.Should().Equal(true); store.SaveCalls.Should().Be(0); initial.Get(Id).Enabled.Should().BeTrue();
        var applied = session.Capture(); model.Rows.Single(r => r.RuleId == Id).Enabled = true;
        applied.Get(Id).Enabled.Should().BeFalse(); await model.SaveAsync();
        session.Capture().Should().BeSameAs(applied); model.ChangeCount.Should().Be(0); store.SaveCalls.Should().Be(1);
        model.Apply(); model.Apply(); changes.Should().Equal(true, true, false);
    }

    [Fact]
    public async Task FailedLoadOrSave_LeavesDraftBaselineAndActiveConfigurationIntact()
    {
        var store = new Store(); var session = new RuleConfigurationSession(RuleConfigurationDocument.Defaults);
        using var model = new RuleConfigurationViewModel(session, store: store); await model.InitializeAsync();
        model.Rows.Single(r => r.RuleId == Id).Enabled = true; var draft = model.Draft;
        store.Failure = new InvalidDataException("$.Rules[2].Enabled invalid"); await model.LoadAsync("invalid.json");
        model.Draft.Should().BeSameAs(draft); model.Status.Should().Contain("$.Rules[2].Enabled");
        store.Failure = null; store.SaveFailure = true; await model.SaveAsync();
        model.Draft.Should().BeSameAs(draft); model.ChangeCount.Should().Be(1); model.Status.Should().Contain("未保存");
        session.Capture().Should().BeSameAs(RuleConfigurationDocument.Defaults);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrClose_LoadCannotReplacePreviousDraftEvenIfProviderIgnoresCancellation(bool close)
    {
        var store = new Store(); using var model = new RuleConfigurationViewModel(new(RuleConfigurationDocument.Defaults), store: store);
        await model.InitializeAsync(); var draft = model.Draft;
        store.BlockLoad = true; var pending = model.LoadAsync("other.json");
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); model.IsBusy.Should().BeTrue(); model.CanApply.Should().BeFalse();
        if (close) model.Dispose(); else model.Cancel();
        store.Document = RuleConfigurationDocument.Defaults; store.Release.TrySetResult(); await pending;
        model.Draft.Should().BeSameAs(draft); model.CanEdit.Should().Be(!close);
    }

    [Fact]
    public async Task Recalculate_RequiresAppliedDraftAndPropagatesCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = new RuleConfigurationViewModel(new(RuleConfigurationDocument.Defaults), recalculate: async token =>
        { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }, canRecalculate: () => true, store: new Store());
        await model.InitializeAsync(); model.CanRecalculate.Should().BeFalse(); model.Apply(); model.CanRecalculate.Should().BeTrue();
        var pending = model.RecalculateAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); model.Cancel(); await pending;
        model.Status.Should().NotContain("已完成"); model.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task InvalidDraft_CannotApplyOrSaveAndCanBeRestored()
    {
        using var model = new RuleConfigurationViewModel(new(RuleConfigurationDocument.Defaults), store: new Store()); await model.InitializeAsync();
        model.Rows.Single(r => r.RuleId == Id).SeveritySelection = "Fatal";
        model.CanApply.Should().BeFalse(); model.CanSave.Should().BeFalse(); model.Status.Should().Contain("SeverityOverride");
        model.RestoreDefaults(); model.CanApply.Should().BeTrue(); model.ChangeCount.Should().Be(1);
    }

    internal sealed class Store : IRuleConfigurationStore
    {
        public RuleConfigurationDocument Document = RuleConfigurationDocument.Parse(CompatibleJson);
        public Exception? Failure;
        public bool SaveFailure, BlockLoad;
        public int SaveCalls;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static RuleConfigurationFile File(RuleConfigurationDocument document) => new(Path.Combine(Path.GetTempPath(), "rules.json"),
            SqlFileSnapshot.FromBytes(Path.Combine(Path.GetTempPath(), "rules.json"), Encoding.UTF8.GetBytes(document.Json)), document);
        public async Task<RuleConfigurationFile> LoadAsync(string? path, CancellationToken token)
        {
            if (Failure != null) throw Failure;
            if (BlockLoad) { Started.TrySetResult(); await Release.Task; }
            return File(Document);
        }
        public Task<RuleConfigurationSaveOutcome> SaveAsync(RuleConfigurationFile file, RuleConfigurationDocument document, CancellationToken token)
        {
            SaveCalls++;
            if (Failure != null) throw Failure;
            return Task.FromResult(SaveFailure ? new RuleConfigurationSaveOutcome(false, null, "未保存：模拟文件冲突") : new(true, File(document), "saved", true));
        }
        public Task<RuleConfigurationSaveOutcome> SaveAsAsync(string path, RuleConfigurationDocument document, CancellationToken token) => SaveAsync(File(document), document, token);
    }
}
