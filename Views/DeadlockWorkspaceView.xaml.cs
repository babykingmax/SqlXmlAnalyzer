using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SqlXmlAnalyzer.Views
{
    public partial class DeadlockWorkspaceView : UserControl
    {
        public DeadlockWorkspaceView()
        {
            InitializeComponent();
            SizeChanged += (_, _) =>
            {
                bool compact = Core.Services.WorkspaceInteractionService.Layout(ActualWidth, ActualHeight).Compact;
                if (_compact == compact) return;
                _compact = compact;
                DeadlockLeftColumn.Width = new GridLength(compact ? 0 : 280);
                DeadlockRightColumn.Width = new GridLength(compact ? 0 : 320);
                ToggleLeftBtn.Content = compact ? "▶ 侧边栏" : "◀ 侧边栏";
                ToggleRightBtn.Content = compact ? "◀ 属性栏" : "属性栏 ▶";
                UpdateMinimumWidth();
            };
        }
        private bool? _compact;
        public void MoveWorkspaceFocus(bool reverse)
        {
            FrameworkElement[] groups = [DeadlockProcessesList, DeadlockResourcesList, DeadlockPatternsListBox];
            int current = System.Array.FindIndex(groups, element => element.IsKeyboardFocusWithin);
            int next = Core.Services.WorkspaceInteractionService.MoveSelection(groups.Length, current, reverse ? -1 : 1);
            if (next < 2) DeadlockLeftColumn.Width = new GridLength(280);
            else DeadlockRightColumn.Width = new GridLength(320);
            ToggleLeftBtn.Content = DeadlockLeftColumn.Width.Value > 0 ? "◀ 侧边栏" : "▶ 侧边栏";
            ToggleRightBtn.Content = DeadlockRightColumn.Width.Value > 0 ? "属性栏 ▶" : "◀ 属性栏";
            UpdateMinimumWidth();
            Services.WorkspaceAccessibility.Focus(groups[next]);
        }

        private void UpdateMinimumWidth() => ((Grid)WorkspaceScroll.Content).MinWidth =
            System.Math.Max(520, DeadlockLeftColumn.Width.Value + DeadlockRightColumn.Width.Value + 320);

        public ColumnDefinition LeftColumn => DeadlockLeftColumn;
        public ColumnDefinition RightColumn => DeadlockRightColumn;
        public ListView ProcessesList => DeadlockProcessesList;
        public ListView ResourcesList => DeadlockResourcesList;
        public ListBox PatternsListBox => DeadlockPatternsListBox;
        public ComboBox XelSelector => XelDeadlockSelector;
        public Button ToggleLeftButton => ToggleLeftBtn;
        public Button ToggleRightButton => ToggleRightBtn;
        public ToggleButton PlaybackModeToggleButton => PlaybackModeToggle;
        public Border CanvasBorder => DeadlockCanvasBorder;
        public Canvas GraphCanvas => DeadlockGraphCanvas;
        public ScaleTransform ScaleTransform => DeadlockScaleTransform;
        public TranslateTransform TranslateTransform => DeadlockTranslateTransform;
        public DeadlockPlaybackControl Playback => PlaybackControl;

        public event SelectionChangedEventHandler? ProcessesSelectionChanged;
        public event SelectionChangedEventHandler? ResourcesSelectionChanged;
        public event SelectionChangedEventHandler? XelSelectionChanged;
        public event RoutedEventHandler? XelSearchRequested;
        public event RoutedEventHandler? ToggleLeftClicked;
        public event RoutedEventHandler? ToggleRightClicked;
        public event RoutedEventHandler? ZoomToFitClicked;
        public event RoutedEventHandler? PlaybackModeChecked;
        public event RoutedEventHandler? PlaybackModeUnchecked;
        public event RoutedEventHandler? CopyDeadlockMermaidClicked;
        public event RoutedEventHandler? OpenDeadlockMermaidClicked;
        public event SelectionChangedEventHandler? PatternsSelectionChanged;

        private void DeadlockProcessesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ProcessesSelectionChanged?.Invoke(sender, e);

        private void DeadlockResourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ResourcesSelectionChanged?.Invoke(sender, e);

        private void XelDeadlockSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            XelSelectionChanged?.Invoke(sender, e);

        private void XelSearch_Click(object sender, RoutedEventArgs e) => XelSearchRequested?.Invoke(sender, e);

        private void ToggleLeft_Click(object sender, RoutedEventArgs e)
        {
            ToggleLeftClicked?.Invoke(sender, e);
            UpdateMinimumWidth();
        }

        private void ToggleRight_Click(object sender, RoutedEventArgs e)
        {
            ToggleRightClicked?.Invoke(sender, e);
            UpdateMinimumWidth();
        }

        private void ZoomToFitDeadlock_Click(object sender, RoutedEventArgs e) =>
            ZoomToFitClicked?.Invoke(sender, e);

        private void PlaybackModeToggle_Checked(object sender, RoutedEventArgs e) =>
            PlaybackModeChecked?.Invoke(sender, e);

        private void PlaybackModeToggle_Unchecked(object sender, RoutedEventArgs e) =>
            PlaybackModeUnchecked?.Invoke(sender, e);

        private void CopyDeadlockMermaid_Click(object sender, RoutedEventArgs e) =>
            CopyDeadlockMermaidClicked?.Invoke(sender, e);

        private void OpenDeadlockMermaidInBrowser_Click(object sender, RoutedEventArgs e) =>
            OpenDeadlockMermaidClicked?.Invoke(sender, e);

        private void DeadlockPatternsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            PatternsSelectionChanged?.Invoke(sender, e);
    }
}
