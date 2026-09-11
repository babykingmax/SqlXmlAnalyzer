using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using System.ComponentModel;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Views;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanWorkspaceVisualLayoutTests
{
    [Theory]
    [InlineData(261.6)]
    [InlineData(360)]
    [InlineData(480)]
    [InlineData(700)]
    public void WorkspaceHeight_KeepsAtLeastSixtyFivePercentForTheGraphAndBothViewsReachable(double height) => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var view = CreateView();
        Arrange(view, 900, height);
        view.NodifyGraph.ActualHeight.Should().BeGreaterThanOrEqualTo(height * 0.65);
        ((Expander)view.FindName("AuxiliaryPanel")).ActualHeight.Should().BeApproximately(24, 0.1);
        var hotspots = (Button)view.FindName("HotspotsPaneButton");
        hotspots.Visibility.Should().Be(height < 420 ? Visibility.Visible : Visibility.Collapsed);
        if (height < 420)
        {
            hotspots.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Arrange(view, 900, height);
            view.GraphTabControl.SelectedIndex.Should().Be(1);
            view.RecostDataGrid.ActualHeight.Should().BeGreaterThan(0);
            ((Button)view.FindName("GraphPaneButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Arrange(view, 900, height);
            view.GraphTabControl.SelectedIndex.Should().Be(0);
            view.NodifyGraph.ActualHeight.Should().BeGreaterThanOrEqualTo(height * 0.65);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void SelectedDetailsTab_KeepsBodyTextColorSeparateFromItsAccentHeader() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var view = CreateView();
        view.Resources["TextBrush"] = System.Windows.Media.Brushes.Black;
        view.Resources["AccentBrush"] = System.Windows.Media.Brushes.Blue;
        Arrange(view, 1500);
        var tab = (TabItem)view.FindName("StructuredDetailsTab");
        tab.IsSelected.Should().BeTrue();
        tab.Foreground.Should().BeSameAs(System.Windows.Media.Brushes.Black);
        return Task.CompletedTask;
    });

    [Fact]
    public void Workspace_ResizesWithoutAnOuterHorizontalScrollerAndKeepsAuxiliaryContentCollapsed() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var view = CreateView();
        Arrange(view, 1500);
        view.GraphTabControl.Items.Count.Should().Be(2);
        ((TabItem)view.GraphTabControl.Items[0]).Header.Should().Be("计划图");
        view.ContentGrid.ColumnDefinitions[0].ActualWidth.Should().BeApproximately(280, 0.1);
        view.ContentGrid.ColumnDefinitions[4].ActualWidth.Should().BeApproximately(320, 0.1);
        ((Expander)view.FindName("AuxiliaryPanel")).IsExpanded.Should().BeFalse();
        view.GraphTabControl.ActualHeight.Should().BeGreaterThan(700 * 0.65);
        view.FindName("WorkspaceScroll").Should().BeNull();

        Arrange(view, 1250);
        ((Expander)view.FindName("LeftPanel")).Visibility.Should().Be(Visibility.Visible);
        ((Expander)view.FindName("RightPanel")).Visibility.Should().Be(Visibility.Collapsed);

        Arrange(view, 900);
        view.GraphTabControl.Visibility.Should().Be(Visibility.Visible);
        ((Expander)view.FindName("LeftPanel")).Visibility.Should().Be(Visibility.Collapsed);
        ((Button)view.FindName("IssuesPaneButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Arrange(view, 900);
        view.GraphTabControl.Visibility.Should().Be(Visibility.Collapsed);
        ((Expander)view.FindName("LeftPanel")).Visibility.Should().Be(Visibility.Visible);
        view.ContentGrid.ColumnDefinitions[0].ActualWidth.Should().BeGreaterThan(880);
        view.ShowGraph();
        view.GraphTabControl.Visibility.Should().Be(Visibility.Visible);
        return Task.CompletedTask;
    });

    [Fact]
    public void Preferences_RestoreColumnsAndAuxiliarySectionsWithoutAddingCentralTabs() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var view = CreateView();
        view.ApplyPreferences(new()
        {
            ActiveView = "operators",
            LeftWidth = 350,
            RightWidth = 390,
            Columns = [new("对象", 245, 1, true), new("逻辑操作", 175, 2, true)]
        });
        Arrange(view, 1600);
        view.GraphTabControl.SelectedIndex.Should().Be(1);
        view.RecostDataGrid.Columns.Single(column => Equals(column.Header, "对象")).Width.Value.Should().Be(245);
        view.RecostDataGrid.Columns.Single(column => Equals(column.Header, "逻辑操作")).Visibility.Should().Be(Visibility.Visible);
        view.OpenAuxiliary("rewrite");
        ((TabItem)view.FindName("SqlRefactorTab")).IsSelected.Should().BeTrue();
        ((Expander)view.FindName("AuxiliaryPanel")).IsExpanded.Should().BeTrue();
        view.GraphTabControl.Items.Count.Should().Be(2);
        var preferences = view.CapturePreferences();
        preferences.LeftWidth.Should().Be(350);
        preferences.RightWidth.Should().Be(390);
        preferences.AuxiliaryOpen.Should().BeTrue();
        preferences.ActiveView.Should().Be("operators");
        return Task.CompletedTask;
    });

    [Fact]
    public void FirstLoadAndReset_KeepIdentityAndOperatorColumnsOnTheLeft() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var view = CreateView(applyDefaults: false);
        view.CapturePreferences().Columns.Select(column => column.DisplayIndex)
            .Should().Equal(Enumerable.Range(0, view.RecostDataGrid.Columns.Count));
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Arrange(view, 1500);
        string[] expected = ["Node ID", "物理操作", "对象", "估算成本", "实际行 / 次", "估算行 / 次", "估算偏差", "问题"];
        VisibleColumns(view).Should().Equal(expected);
        view.RecostDataGrid.Columns.Single(column => Equals(column.Header, "问题")).DisplayIndex = 0;
        view.ApplyPreferences(new());
        Arrange(view, 1500);
        VisibleColumns(view).Should().Equal(expected);
        return Task.CompletedTask;
    });

    [Fact]
    public void SavedColumnOrder_RestoresReorderedAndOptionalColumnsWithoutReversingThem() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var view = CreateView();
        Arrange(view, 1500);
        view.RecostDataGrid.Columns.Single(column => Equals(column.Header, "对象")).DisplayIndex = 1;
        view.RecostDataGrid.Columns.Single(column => Equals(column.Header, "问题")).DisplayIndex = 3;
        var optional = view.RecostDataGrid.Columns.Single(column => Equals(column.Header, "读取放大"));
        optional.Visibility = Visibility.Visible;
        optional.Width = new DataGridLength(145);
        var saved = view.CapturePreferences();
        view.PreferencesStore.Save(saved).Should().BeTrue();
        var restored = CreateView(applyDefaults: false);
        restored.PreferencesStore = view.PreferencesStore;
        restored.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Arrange(restored, 1500);
        VisibleColumns(restored).Should().Equal(VisibleColumns(view));
        restored.CapturePreferences().Columns.OrderBy(column => column.DisplayIndex).Select(column => column.Key)
            .Should().Equal(saved.Columns.OrderBy(column => column.DisplayIndex).Select(column => column.Key));
        restored.RecostDataGrid.Columns.Single(column => Equals(column.Header, "读取放大")).Width.Value.Should().Be(145);
        return Task.CompletedTask;
    });

    [Fact]
    public void EvidenceDetails_AreScrollableAndExposeMissingValueStateAndSource() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var view = CreateView();
        string details = string.Join(Environment.NewLine, Enumerable.Range(1, 80).Select(index => $"证据说明第 {index} 行"));
        object Model(string? value) => new
        {
            PlanWorkspace = new EvidenceWorkspaceFixture
            {
                SourceDetails = details,
                SelectedEvidence = new EvidenceItemFixture { Value = value, State = "Missing", Source = "RunTimeCountersPerThread.ActualRows" }
            }
        };
        view.DataContext = Model(null);
        view.OpenAuxiliary("evidence");
        Arrange(view, 1500);
        var source = (TextBox)view.FindName("EvidenceSourceDetails");
        source.IsReadOnly.Should().BeTrue();
        source.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
        source.Text.Should().Contain("证据说明第 80 行");
        AutomationProperties.GetName(source).Should().Be("完整证据与来源说明");
        source.ScrollToEnd();
        source.UpdateLayout();
        source.VerticalOffset.Should().BeGreaterThan(0, "the end of long source details remains reachable");
        ((TextBox)view.FindName("EvidenceSql")).ActualHeight.Should().BeGreaterThan(20);
        ((TextBox)view.FindName("EvidenceXml")).ActualHeight.Should().BeGreaterThan(20);
        ((TextBox)view.FindName("SelectedEvidenceValue")).Text.Should().Be("未采集");
        ((TextBlock)view.FindName("SelectedEvidenceState")).Text.Should().Be("Missing");
        ((TextBlock)view.FindName("SelectedEvidenceSource")).Text.Should().Be("RunTimeCountersPerThread.ActualRows");
        view.DataContext = Model("0");
        Arrange(view, 1500);
        ((TextBox)view.FindName("SelectedEvidenceValue")).Text.Should().Be("0", "a measured zero is not missing evidence");
        return Task.CompletedTask;
    });

    [Fact]
    public void Hotspots_WhenStatementReplacesItems_PreserveTheActiveNumericOrUserSort() => PlanGraphAsyncInteractionTests.RunSta(() =>
    {
        var view = CreateView();
        view.GraphTabControl.SelectedIndex = 1;
        var grid = view.RecostDataGrid;
        grid.ItemsSource = new[]
        {
            new SqlXmlAnalyzer.PlanNodeViewModel { NodeId = "first-low", PhysicalOp = "Zulu", OwnCost = 2 },
            new SqlXmlAnalyzer.PlanNodeViewModel { NodeId = "first-high", PhysicalOp = "Alpha", OwnCost = 30 }
        };
        grid.Items.Cast<SqlXmlAnalyzer.PlanNodeViewModel>().Select(node => node.NodeId).Should().Equal("first-high", "first-low");
        grid.ItemsSource = new[]
        {
            new SqlXmlAnalyzer.PlanNodeViewModel { NodeId = "second-low", PhysicalOp = "Zulu", OwnCost = 1 },
            new SqlXmlAnalyzer.PlanNodeViewModel { NodeId = "second-high", PhysicalOp = "Alpha", OwnCost = 10 }
        };
        grid.Items.Cast<SqlXmlAnalyzer.PlanNodeViewModel>().Select(node => node.NodeId).Should().Equal("second-high", "second-low");

        var collection = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        collection.SortDescriptions.Clear();
        collection.SortDescriptions.Add(new SortDescription("PhysicalOp", ListSortDirection.Ascending));
        grid.ItemsSource = new[]
        {
            new SqlXmlAnalyzer.PlanNodeViewModel { NodeId = "third-high", PhysicalOp = "Zulu", OwnCost = 50 },
            new SqlXmlAnalyzer.PlanNodeViewModel { NodeId = "third-low", PhysicalOp = "Alpha", OwnCost = 2 }
        };
        grid.Items.Cast<SqlXmlAnalyzer.PlanNodeViewModel>().Select(node => node.NodeId).Should().Equal("third-low", "third-high");
        CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Clear();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        grid.ItemsSource = new[]
        {
            new SqlXmlAnalyzer.PlanNodeViewModel { NodeId = "unsorted-first", PhysicalOp = "Zulu", OwnCost = 1 },
            new SqlXmlAnalyzer.PlanNodeViewModel { NodeId = "unsorted-second", PhysicalOp = "Alpha", OwnCost = 50 }
        };
        grid.Items.Cast<SqlXmlAnalyzer.PlanNodeViewModel>().Select(node => node.NodeId).Should().Equal("unsorted-first", "unsorted-second");
        view.GraphTabControl.SelectedIndex.Should().Be(1);
        return Task.CompletedTask;
    });

    private static string[] VisibleColumns(PlanWorkspaceView view) => view.RecostDataGrid.Columns
        .Where(column => column.Visibility == Visibility.Visible).OrderBy(column => column.DisplayIndex)
        .Select(column => column.Header.ToString()!).ToArray();

    private static PlanWorkspaceView CreateView(bool applyDefaults = true)
    {
        if (System.Windows.Application.Current == null)
        {
            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string key in new[] { "MaterialDesignFlatButton", "MaterialDesignRaisedButton", "MaterialDesignOutlinedButton", "SecondaryButtonStyle" })
            {
                var style = new Style(typeof(Button));
                style.Seal();
                application.Resources[key] = style;
            }
        }
        var view = new PlanWorkspaceView
        {
            PreferencesStore = new PlanWorkspacePreferencesStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "SqlXmlAnalyzer-layout-tests", Guid.NewGuid().ToString("N") + ".json"))
        };
        if (applyDefaults) view.ApplyPreferences(new());
        return view;
    }

    public sealed class EvidenceWorkspaceFixture
    {
        private EvidenceItemFixture? _selectedEvidence;
        public string SourceDetails { get; init; } = "";
        // Match the real VM: transient null selections during an ItemsSource refresh are ignored.
        public EvidenceItemFixture? SelectedEvidence { get => _selectedEvidence; set { if (value != null) _selectedEvidence = value; } }
        public IReadOnlyList<EvidenceItemFixture> Evidence => SelectedEvidence == null ? [] : [SelectedEvidence];
        public EvidenceSourceFixture SourceTarget { get; } = new();
    }

    public sealed class EvidenceItemFixture
    {
        public string Name => "输出行数";
        public string? Value { get; init; }
        public string State { get; init; } = "Missing";
        public string Source { get; init; } = "";
    }

    public sealed class EvidenceSourceFixture
    {
        public string Sql => "SELECT 1;";
        public string Xml => "<RelOp NodeId='1'/>";
    }

    private static void Arrange(FrameworkElement element, double width, double height = 700)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
}
