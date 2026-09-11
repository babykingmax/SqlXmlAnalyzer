using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class WorkspaceAccessibilityTests
{
    [Theory]
    [InlineData("disabled", false)]
    [InlineData("disabled", true)]
    [InlineData("collapsed", false)]
    [InlineData("collapsed", true)]
    [InlineData("nonfocusable", false)]
    [InlineData("nonfocusable", true)]
    [InlineData("rejected", false)]
    [InlineData("rejected", true)]
    public void MoveFocus_SkipsUnavailableOrRejectedGroups(string unavailable, bool reverse) => WithWindow((window, panel) =>
    {
        Button[] groups = [new(), new(), new(), new()];
        foreach (var button in groups) panel.Children.Add(button);
        foreach (var button in groups.Skip(1).Take(2))
        {
            if (unavailable == "disabled") button.IsEnabled = false;
            if (unavailable == "collapsed") button.Visibility = Visibility.Collapsed;
            if (unavailable == "nonfocusable") button.Focusable = false;
            if (unavailable == "rejected") button.PreviewGotKeyboardFocus += (_, e) => e.Handled = true;
        }
        Show(window);
        WorkspaceAccessibility.Focus(groups[reverse ? 3 : 0]).Should().BeTrue();
        WorkspaceAccessibility.MoveFocus(groups, reverse).Should().BeTrue();
        groups[reverse ? 0 : 3].IsKeyboardFocusWithin.Should().BeTrue();
        // Continue through the wrap boundary in both directions.
        WorkspaceAccessibility.MoveFocus(groups, reverse).Should().BeTrue();
        groups[reverse ? 3 : 0].IsKeyboardFocusWithin.Should().BeTrue();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveFocus_WithoutCurrentGroupStartsAtEligibleBoundary(bool reverse) => WithWindow((window, panel) =>
    {
        Button[] groups = [new() { IsEnabled = false }, new(), new(), new() { IsEnabled = false }];
        var origin = new Button();
        panel.Children.Add(origin);
        foreach (var button in groups) panel.Children.Add(button);
        Show(window);
        WorkspaceAccessibility.Focus(origin).Should().BeTrue();
        WorkspaceAccessibility.MoveFocus(groups, reverse).Should().BeTrue();
        groups[reverse ? 2 : 1].IsKeyboardFocusWithin.Should().BeTrue();
    });

    [Fact]
    public void MoveFocus_EmptyOrEntirelyUnavailableGroupsPreserveFocus() => WithWindow((window, panel) =>
    {
        var origin = new Button();
        var disabled = new Button { IsEnabled = false };
        var rejected = new Button();
        rejected.PreviewGotKeyboardFocus += (_, e) => e.Handled = true;
        panel.Children.Add(origin); panel.Children.Add(disabled); panel.Children.Add(rejected);
        Show(window);
        WorkspaceAccessibility.Focus(origin).Should().BeTrue();
        WorkspaceAccessibility.MoveFocus([], false).Should().BeFalse();
        WorkspaceAccessibility.MoveFocus([disabled, rejected], false).Should().BeFalse();
        WorkspaceAccessibility.MoveFocus([disabled, rejected], true).Should().BeFalse();
        origin.IsKeyboardFocusWithin.Should().BeTrue();
        WorkspaceAccessibility.Focus(new Button()).Should().BeFalse("a detached target is not visible");
    });

    [Fact]
    public void Focus_AfterExpandingDetailsArrangesAndScrollsTargetIntoView() => WithWindow((window, panel) =>
    {
        var details = new DataGrid { Height = 200, Width = 260 };
        var expander = new Expander { IsExpanded = false, Content = details };
        var content = new StackPanel { Width = 1200, Orientation = Orientation.Horizontal };
        content.Children.Add(new Border { Width = 850 }); content.Children.Add(expander);
        var scroll = new ScrollViewer { Width = 300, Height = 240, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
        panel.Children.Add(scroll);
        Show(window);
        WorkspaceAccessibility.Focus(details).Should().BeFalse();
        expander.IsExpanded = true;
        WorkspaceAccessibility.Focus(details).Should().BeTrue();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        details.IsKeyboardFocusWithin.Should().BeTrue();
        scroll.HorizontalOffset.Should().BeGreaterThan(0);
    });

    private static void Show(Window window)
    {
        window.Show(); window.Activate(); window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static void WithWindow(Action<Window, StackPanel> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var panel = new StackPanel();
                window = new Window { Content = panel, Width = 400, Height = 300, Left = -20000, Top = 0,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
                test(window, panel);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("WPF focus tests must terminate");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
