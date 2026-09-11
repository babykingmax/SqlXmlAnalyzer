using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Services;

public static class WorkspaceAccessibility
{
    public static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    public static bool Focus(FrameworkElement element)
    {
        if (!element.IsEnabled || !element.Focusable) return false;
        // Expanding an ancestor or selecting a tab invalidates layout. Scroll only
        // after the target has an arranged slot, so focus is also on screen.
        element.UpdateLayout();
        if (!element.IsVisible) return false;
        element.BringIntoView();
        Keyboard.Focus(element);
        return element.IsKeyboardFocusWithin;
    }

    public static bool MoveFocus(IReadOnlyList<FrameworkElement> groups, bool reverse)
    {
        int current = -1;
        for (int i = 0; i < groups.Count; i++)
            if (groups[i].IsKeyboardFocusWithin) { current = i; break; }

        // A target can reject focus even when enabled (for example through a
        // PreviewGotKeyboardFocus handler). Stop only after focus really moves.
        for (int visited = 0; visited < groups.Count; visited++)
        {
            current = Core.Services.WorkspaceInteractionService.MoveSelection(groups.Count, current, reverse ? -1 : 1);
            if (Focus(groups[current])) return true;
        }
        return false;
    }

    public static void PrepareDialog(Window dialog)
    {
        var previous = Keyboard.FocusedElement;
        dialog.Loaded += (_, _) =>
        {
            double width = Math.Max(320, Math.Min(SystemParameters.WorkArea.Width, dialog.Owner?.ActualWidth ?? SystemParameters.WorkArea.Width));
            double height = Math.Max(240, Math.Min(SystemParameters.WorkArea.Height, dialog.Owner?.ActualHeight ?? SystemParameters.WorkArea.Height));
            double contentWidth = Math.Max(dialog.MinWidth, double.IsNaN(dialog.Width) ? dialog.ActualWidth : dialog.Width);
            double contentHeight = Math.Max(dialog.MinHeight, double.IsNaN(dialog.Height) ? dialog.ActualHeight : dialog.Height);
            dialog.MinWidth = Math.Min(dialog.MinWidth, width); dialog.MinHeight = Math.Min(dialog.MinHeight, height);
            dialog.MaxWidth = width; dialog.MaxHeight = height;
            if (dialog.Content is FrameworkElement content && dialog.Content is not ScrollViewer)
            {
                dialog.Content = null;
                content.MinWidth = Math.Max(content.MinWidth, contentWidth);
                content.MinHeight = Math.Max(content.MinHeight, Math.Max(0, contentHeight - 40));
                dialog.Content = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = content };
            }
        };
        dialog.PreviewKeyDown += (_, e) =>
        {
            // Let the first Escape dismiss an open selector before closing its window.
            if (e.Key == Key.Escape && !Descendants(dialog).OfType<ComboBox>().Any(combo => combo.IsDropDownOpen))
            { dialog.Close(); e.Handled = true; }
        };
        dialog.Closed += (_, _) =>
        {
            if (previous is FrameworkElement element && element.IsVisible && element.IsEnabled)
                element.Dispatcher.BeginInvoke(new Action(() => Focus(element)));
        };
    }

    public static void Report(Exception exception, string operation, FrameworkElement owner)
    {
        string detail = ExceptionPolicy.Describe(exception, "IMP25." + operation);
        if (Window.GetWindow(owner)?.DataContext is Core.ViewModels.MainViewModel model) model.StatusText = detail;
        else MessageBox.Show(detail, "界面操作失败");
    }
}
