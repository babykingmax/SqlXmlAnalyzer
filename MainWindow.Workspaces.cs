using System.Windows.Controls;
using SqlXmlAnalyzer.Views;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
        private ShellNavigationRail NavigationRail => MainShell.Navigation;
        private ShellStatusBar ShellStatus => MainShell.StatusBar;
        private MainWorkspaceView MainWorkspace => MainShell.Workspace;
        private TabControl MainTabControl => MainWorkspace.Tabs;
        private DeadlockWorkspaceView DeadlockWorkspace => MainWorkspace.Deadlock;
        private PlanWorkspaceView PlanWorkspace => MainWorkspace.Plan;
        private PlanComparisonWorkspaceView PlanComparisonWorkspace => MainWorkspace.PlanComparison;
    }
}
