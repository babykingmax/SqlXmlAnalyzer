using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class DeadlockEventSelectionTests
{
    [Fact]
    public void TypedSelection_ReceivesOriginalEventAndInputBeforeInitialNotification()
    {
        RunSta(() =>
        {
            var input = new InputRecognitionService().Parse(InputRecognitionTests.Fixture("deadlock_multiple_events.xdl"));
            using var sessions = new AnalysisSessionCoordinator(); var selector = new ComboBox();
            var selected = new List<DeadlockInput>();
            var service = new XelDeadlockUiActionService(new XelReader(), sessions, selector, new TabControl(),
                (_, _, _) => throw new InvalidOperationException("The typed path must not serialize and reparse the event."),
                (item, context, _) => { context.Should().BeSameAs(input); selected.Add(item); return Task.CompletedTask; });
            selector.SelectionChanged += (_, _) => service.HandleSelectionChangedAsync().GetAwaiter().GetResult();
            service.ShowDocumentEvents(input, "events.xdl"); selector.SelectedIndex = 1;
            selected.Should().HaveCount(2);
            selected[0].Should().BeSameAs(input.Deadlocks[0]); selected[1].Should().BeSameAs(input.Deadlocks[1]);
            selected[1].OriginalElement!.Document.Should().BeSameAs(input.Document);
            service.ClearEvents(); service.CurrentInput.Should().BeNull();
        });
    }

    [Fact]
    public void PartialDocument_SelectorRetainsContractAndSkippedRangeWhileSelectingValidEvents()
    {
        RunSta(() =>
        {
            var selector = new ComboBox { Width = 450, Height = 36 };
            using var sessions = new AnalysisSessionCoordinator();
            string? selectedName = null;
            var service = new XelDeadlockUiActionService(new XelReader(), sessions, selector, new TabControl(),
                (_, _, name) => { selectedName = name; return Task.CompletedTask; });
            selector.SelectionChanged += (_, _) => service.HandleSelectionChangedAsync().GetAwaiter().GetResult();
            var result = new InputRecognitionService().Parse("<deadlock-list><unknown/><deadlock><process-list><process id='p1'/></process-list><resource-list><keylock/></resource-list></deadlock></deadlock-list>");
            service.ShowDocumentEvents(result, "partial.xdl");
            service.CurrentInput.Should().BeSameAs(result);
            selectedName.Should().Contain("死锁事件 2");
            selector.ToolTip!.ToString().Should().Contain("Partial").And.Contain("unknown[1]");
            selector.Items.Count.Should().Be(1);
            string? directory = Environment.GetEnvironmentVariable("SQLXML_TEST_ARTIFACT_DIR");
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
                var panel = new StackPanel { Width = 740, Background = Brushes.White };
                panel.Children.Add(new TextBlock { Text = "IMP-09 · 部分读取：保留有效事件和未处理范围", FontSize = 20, Margin = new Thickness(16) });
                panel.Children.Add(selector);
                panel.Children.Add(new TextBlock { Text = selector.ToolTip.ToString(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) });
                panel.Measure(new Size(740, 250));
                panel.Arrange(new Rect(0, 0, 740, 250));
                panel.UpdateLayout();
                var bitmap = new RenderTargetBitmap(740, 250, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, "imp09-partial-selector.png"));
                encoder.Save(file);
            }
            service.ClearEvents();
            service.CurrentInput.Should().BeNull();
            selector.ToolTip.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void XmlEventSelection_RefreshUsesExistingSourceFile(int eventIndex)
    {
        RunSta(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-SelectorRefresh-{Guid.NewGuid():N}.xdl");
            try
            {
                File.WriteAllText(path, InputRecognitionTests.Fixture("deadlock_multiple_events.xdl"));
                var selector = new ComboBox();
                using var sessions = new AnalysisSessionCoordinator();
                var viewModel = new Core.ViewModels.MainViewModel();
                var service = new XelDeadlockUiActionService(new XelReader(), sessions, selector, new TabControl(),
                    (_, source, _) => { viewModel.CurrentDeadlockFilePath = source; return Task.CompletedTask; });
                selector.SelectionChanged += (_, _) => service.HandleSelectionChangedAsync().GetAwaiter().GetResult();
                service.ShowXmlEvents(new InputRecognitionService().Load(path).Deadlocks, path);
                selector.SelectedIndex = eventIndex;
                string? refreshedPath = null;
                new DocumentRefreshUiActionService(new DocumentRefreshActionService(),
                    source => refreshedPath = source, viewModel).RefreshDeadlockGraph();
                refreshedPath.Should().Be(path);
                var reopened = new DocumentOpenService().OpenAsync(refreshedPath!).GetAwaiter().GetResult();
                reopened.IsSuccess.Should().BeTrue();
                reopened.Deadlocks.Should().HaveCount(2);
            }
            finally { File.Delete(path); }
        });
    }

    [Fact]
    public void XmlSelector_AllEventsAreVisibleAndSelectable_ThenClearedForNextDocument()
    {
        RunSta(() =>
        {
            var selector = new ComboBox { Width = 400, Height = 36 };
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem());
            var selected = new List<(string Xml, string Source, string Name)>();
            using var sessions = new AnalysisSessionCoordinator();
            var service = new XelDeadlockUiActionService(new XelReader(), sessions, selector, tabs,
                (xml, source, name) => { selected.Add((xml, source, name)); return Task.CompletedTask; });
            selector.SelectionChanged += (_, _) => service.HandleSelectionChangedAsync().GetAwaiter().GetResult();
            var result = new InputRecognitionService().Parse(InputRecognitionTests.Fixture("deadlock_multiple_events.xdl"));

            service.ShowXmlEvents(result.Deadlocks, "events.xml");
            selector.Visibility.Should().Be(Visibility.Visible);
            selector.Items.Count.Should().Be(2);
            selected.Should().ContainSingle();
            selector.SelectedIndex = 1;
            selected.Should().HaveCount(2);
            selected[1].Name.Should().Contain("events.xml").And.Contain("死锁事件 2");
            selected[1].Source.Should().Be("events.xml");
            var analysis = new DeadlockAnalysisService().Analyze(SafeXmlHelper.ParseSafe(selected[1].Xml));
            analysis.Processes.Select(p => p.Id).Should().Equal("p3", "p4");
            RenderSelectorEvidence(selector);

            service.ClearEvents();
            selector.Items.Count.Should().Be(0);
            selector.Visibility.Should().Be(Visibility.Collapsed);
            selected.Should().HaveCount(2, "clearing the selector must not analyze another event");
        });
    }

    [Fact]
    public void Selector_ChangingFromXelToXml_UsesTheNewSourceAndDisplayLabel()
    {
        RunSta(() =>
        {
            var selector = new ComboBox();
            using var sessions = new AnalysisSessionCoordinator();
            string? label = null;
            var service = new XelDeadlockUiActionService(new XelReader(), sessions, selector, new TabControl(),
                (_, _, name) => { label = name; return Task.CompletedTask; });
            selector.ItemsSource = new[] { new XelDeadlockReport { Timestamp = "older", DeadlockXml = "<old/>" } };
            selector.SelectedIndex = 0;
            service.HandleSelectionChangedAsync().GetAwaiter().GetResult();
            label.Should().Contain("XEL");
            var result = new InputRecognitionService().Parse(InputRecognitionTests.Fixture("deadlock_multiple_events.xdl"));
            service.ShowXmlEvents(result.Deadlocks, "new.xml");
            service.HandleSelectionChangedAsync().GetAwaiter().GetResult();
            label.Should().Contain("new.xml").And.Contain("死锁事件 1");
            selector.DisplayMemberPath.Should().Be(nameof(DeadlockInput.DisplayName));
        });
    }

    private static void RenderSelectorEvidence(ComboBox selector)
    {
        string? directory = Environment.GetEnvironmentVariable("SQLXML_TEST_ARTIFACT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var panel = new StackPanel { Width = 520, Background = Brushes.White, Margin = new Thickness(0) };
        panel.Children.Add(new TextBlock { Text = "XML 死锁事件选择 · 已选择第 2 / 2 条", FontSize = 18, Margin = new Thickness(16) });
        panel.Children.Add(selector);
        foreach (DeadlockInput input in selector.Items)
            panel.Children.Add(new TextBlock { Text = input.DisplayName, Margin = new Thickness(16, 8, 16, 0) });
        panel.Measure(new Size(520, 190));
        panel.Arrange(new Rect(0, 0, 520, 190));
        panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap(520, 190, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(panel);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, "xml-event-selector.png"));
        encoder.Save(stream);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
