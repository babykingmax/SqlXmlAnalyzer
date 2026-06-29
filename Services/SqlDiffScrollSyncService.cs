using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class SqlDiffScrollSyncService
    {
        private readonly DependencyObject _originalHost;
        private readonly DependencyObject _refactoredHost;
        private ScrollViewer? _originalScroll;
        private ScrollViewer? _refactoredScroll;
        private bool _isSynchronizing;

        public SqlDiffScrollSyncService(
            DependencyObject originalHost,
            DependencyObject refactoredHost)
        {
            _originalHost = originalHost ?? throw new ArgumentNullException(nameof(originalHost));
            _refactoredHost = refactoredHost ?? throw new ArgumentNullException(nameof(refactoredHost));
        }

        public void Attach()
        {
            if (_originalScroll != null)
            {
                _originalScroll.ScrollChanged -= OriginalScrollChanged;
            }

            if (_refactoredScroll != null)
            {
                _refactoredScroll.ScrollChanged -= RefactoredScrollChanged;
            }

            _originalScroll = FindVisualChild<ScrollViewer>(_originalHost);
            _refactoredScroll = FindVisualChild<ScrollViewer>(_refactoredHost);

            if (_originalScroll != null)
            {
                _originalScroll.ScrollChanged += OriginalScrollChanged;
            }

            if (_refactoredScroll != null)
            {
                _refactoredScroll.ScrollChanged += RefactoredScrollChanged;
            }
        }

        private void OriginalScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            SyncScroll(_refactoredScroll, e.VerticalOffset, e.HorizontalOffset);
        }

        private void RefactoredScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            SyncScroll(_originalScroll, e.VerticalOffset, e.HorizontalOffset);
        }

        private void SyncScroll(ScrollViewer? target, double verticalOffset, double horizontalOffset)
        {
            if (_isSynchronizing || target == null)
            {
                return;
            }

            _isSynchronizing = true;
            try
            {
                target.ScrollToVerticalOffset(verticalOffset);
                target.ScrollToHorizontalOffset(horizontalOffset);
            }
            finally
            {
                _isSynchronizing = false;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject? depObj)
            where T : DependencyObject
        {
            if (depObj == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T matchedChild)
                {
                    return matchedChild;
                }

                T? childItem = FindVisualChild<T>(child);
                if (childItem != null)
                {
                    return childItem;
                }
            }

            return null;
        }
    }
}
