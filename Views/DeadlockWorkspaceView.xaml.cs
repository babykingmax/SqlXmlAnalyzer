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
        }

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

        private void ToggleLeft_Click(object sender, RoutedEventArgs e) =>
            ToggleLeftClicked?.Invoke(sender, e);

        private void ToggleRight_Click(object sender, RoutedEventArgs e) =>
            ToggleRightClicked?.Invoke(sender, e);

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
