using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SqlXmlAnalyzer.Views
{
    public partial class PlanComparisonWorkspaceView : UserControl
    {
        public PlanComparisonWorkspaceView()
        {
            InitializeComponent();
            TuningHistoryListView.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                TuningHistoryMouseDoubleClicked?.Invoke(TuningHistoryListView, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
                e.Handled = true;
            };
        }
        public void MoveWorkspaceFocus(bool reverse)
        {
            FrameworkElement[] groups = [TuningHistoryListView, SetSelectedPlanA, SetSelectedPlanB, PlanATreeView, PlanBTreeView];
            Services.WorkspaceAccessibility.MoveFocus(groups, reverse);
        }

        public ListView TuningHistoryList => TuningHistoryListView;
        public TreeView PlanATree => PlanATreeView;
        public TreeView PlanBTree => PlanBTreeView;

        public event MouseButtonEventHandler? TuningHistoryMouseDoubleClicked;
        public event RoutedEventHandler? SaveSessionClicked;
        public event RoutedEventHandler? LoadSessionClicked;
        public event RoutedEventHandler? SwapPlanABClicked;

        private void TuningHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            TuningHistoryMouseDoubleClicked?.Invoke(sender, e);

        private void SaveSession_Click(object sender, RoutedEventArgs e) =>
            SaveSessionClicked?.Invoke(sender, e);

        private void LoadSession_Click(object sender, RoutedEventArgs e) =>
            LoadSessionClicked?.Invoke(sender, e);

        private void SwapPlanAB_Click(object sender, RoutedEventArgs e) =>
            SwapPlanABClicked?.Invoke(sender, e);
    }
}
