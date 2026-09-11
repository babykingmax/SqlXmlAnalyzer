using System.IO;
using System.Windows.Controls;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class XelSearchViewModelTests
{
    [Fact]
    public async Task SelectGroupAndEvent_NavigatesExactSourceWithOriginalMetadata()
    {
        XelSearchEvent? navigated = null;
        using var model = new XelSearchViewModel(entry => { navigated = entry; return Task.CompletedTask; }, new Service());
        await model.InitializeAsync(null);
        model.Groups.Should().ContainSingle(); model.SelectedGroup = model.Groups[0]; model.SelectedEvent = model.VisibleEvents[1];
        model.Details.Should().Contain("b.xel").And.Contain("SHA-256").And.Contain("子事件 1");
        await model.NavigateAsync(); navigated.Should().BeSameAs(model.SelectedEvent.Entry);
        model.SelectedGroup = null; model.SelectedEvent.Should().BeNull(); model.CanNavigate.Should().BeFalse();
    }

    [Fact]
    public async Task InvalidFilter_PreservesPreviousResultAndDoesNotDump()
    {
        var reporter = new Reporter(); using var model = new XelSearchViewModel(_ => Task.CompletedTask, new Service(), reporter);
        await model.InitializeAsync(null); var events = model.Events;
        model.From = "2026-09-10T08:00:00"; await model.SearchAsync();
        model.Events.Should().BeSameAs(events); model.Status.Should().Contain("时区"); reporter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("+14:01")]
    [InlineData("+25:00")]
    [InlineData("08:00")]
    public async Task InvalidDisplayOffset_IsExpectedFailure(string offset)
    {
        var reporter = new Reporter(); using var model = new XelSearchViewModel(_ => Task.CompletedTask, new Service(), reporter);
        await model.InitializeAsync(null); model.Offset = offset; await model.SearchAsync();
        model.Status.Should().Contain("操作失败"); reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CancelledImport_RetainsPreviousResultsAndSelection()
    {
        var service = new Service(); using var model = new XelSearchViewModel(_ => Task.CompletedTask, service);
        await model.InitializeAsync(null); var events = model.Events; model.SelectedEvent = events[0];
        service.BlockLoad = true; var pending = model.ImportAsync(["next.xel"]);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.IsBusy.Should().BeTrue(); model.CanNavigate.Should().BeFalse();
        model.Cancel(); service.Release.TrySetResult(); await pending;
        model.Events.Should().BeSameAs(events); model.SelectedEvent.Should().BeSameAs(events[0]); model.Status.Should().Contain("已取消");
    }

    [Fact]
    public async Task ObsoleteImportCannotOverwriteNewSearchEvenWhenReaderIgnoresCancellation()
    {
        var service = new Service(); using var model = new XelSearchViewModel(_ => Task.CompletedTask, service);
        await model.InitializeAsync(null); service.BlockLoad = true;
        var pending = model.ImportAsync(["next.xel"]); await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.Text = "search-marker"; await model.SearchAsync();
        var current = model.Events; model.Events.Should().ContainSingle();
        service.Release.TrySetResult(); await pending;
        model.Events.Should().BeSameAs(current); model.Status.Should().Contain("匹配 1 /");
    }

    [Fact]
    public async Task CloseDuringLoad_CannotCommitLateResult()
    {
        var service = new Service { BlockLoad = true }; var model = new XelSearchViewModel(_ => Task.CompletedTask, service);
        var pending = model.InitializeAsync(null); await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.Dispose(); service.Release.TrySetResult(); await pending;
        model.Events.Should().BeEmpty(); model.CanNavigate.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAndImportDuringInitialization_DoNotCancelOrReplaceInitialSource()
    {
        var service = new Service { BlockLoad = true };
        using var model = new XelSearchViewModel(_ => Task.CompletedTask, service);
        model.CanSearch.Should().BeFalse();
        var pending = model.InitializeAsync(XelSearchServiceTests.Source());
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.CanSearch.Should().BeFalse(); model.CanImport.Should().BeFalse();
        model.Text = "search-marker";
        await model.SearchAsync(); await model.ImportAsync(["new.xel"]);
        model.IsBusy.Should().BeTrue(); model.Events.Should().BeEmpty();
        service.Release.TrySetResult(); await pending;
        model.Events.Should().HaveCount(2); model.CanSearch.Should().BeTrue(); model.CanImport.Should().BeTrue();
        await model.SearchAsync(); model.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task CancelledInitialization_CanRetryImportWithoutClaimingEmptySearchSucceeded()
    {
        var service = new Service { BlockLoad = true };
        using var model = new XelSearchViewModel(_ => Task.CompletedTask, service);
        var pending = model.InitializeAsync(null);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.Cancel(); service.Release.TrySetResult(); await pending;
        await model.SearchAsync(); model.Status.Should().Contain("已取消");
        model.CanSearch.Should().BeFalse(); model.CanImport.Should().BeTrue();
        service.BlockLoad = false; await model.ImportAsync(["retry.xel"]);
        model.Events.Should().HaveCount(2); model.CanSearch.Should().BeTrue();
    }

    [Fact]
    public async Task ChangedSource_NavigationIsBlockedBeforeCallback()
    {
        int calls = 0; var reporter = new Reporter();
        using var model = new XelSearchViewModel(_ => { calls++; return Task.CompletedTask; }, new Service(), reporter);
        await model.InitializeAsync(null); model.SelectedEvent = model.Events[0];
        model.SelectedEvent.Entry.Event.Document.Root!.SetAttributeValue("changed", true);
        await model.NavigateAsync(); calls.Should().Be(0); reporter.Calls.Should().Be(0); model.Status.Should().Contain("已改变");
    }

    [Fact]
    public void ProductionNavigation_UpdatesSelectorToExactMemberAndInvokesAnalysisOnlyOnce()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var entry = XelSearchServiceTests.Catalog().Events[1]; var selector = new ComboBox(); int calls = 0;
            using var sessions = new AnalysisSessionCoordinator();
            var ui = new XelDeadlockUiActionService(new(), sessions, selector, new TabControl(), (_, _, _) => throw new InvalidOperationException("Legacy path must not run"),
                (selected, input, path) =>
                {
                    calls++; selected.Should().BeSameAs(entry.Event); input.Should().BeSameAs(entry.Source.Input); path.Should().Be(entry.SourcePath);
                    return Task.CompletedTask;
                });
            selector.SelectionChanged += (_, _) => ui.HandleSelectionChangedAsync().GetAwaiter().GetResult();
            ui.SelectSearchEventAsync(entry).GetAwaiter().GetResult();
            calls.Should().Be(1); selector.SelectedItem.Should().BeSameAs(entry.Event); ui.CurrentInput.Should().BeSameAs(entry.Source.Input);
        });
    }

    internal sealed class Service : IXelSearchService
    {
        public bool BlockLoad { get; set; }
        public Exception? Failure { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<XelSearchCatalog> LoadAsync(IReadOnlyList<XelSearchSource> existing, IReadOnlyList<string> paths, CancellationToken token)
        {
            if (BlockLoad) { Started.TrySetResult(); await Release.Task; }
            if (Failure != null) throw Failure;
            return XelSearchServiceTests.Catalog();
        }
        public XelSearchResult Search(XelSearchCatalog catalog, XelSearchFilter filter, CancellationToken token) => new XelSearchService().Search(catalog, filter, token);
    }
    internal sealed class Reporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; return new(null, null, "test"); }
    }
}
