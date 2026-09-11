using System.Windows;
using System.Windows.Controls;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Views
{
    public partial class PlanView : UserControl
    {
        public PlanView()
        {
            InitializeComponent();
        }

        private void LeftPanel_Expanded(object sender, System.Windows.RoutedEventArgs e) { }
        private void LeftPanel_Collapsed(object sender, System.Windows.RoutedEventArgs e) { }
        private void PlanOperatorTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e) { }
        private void CopyPlanMermaid_Click(object sender, System.Windows.RoutedEventArgs e) { }
        private void OpenPlanMermaidInBrowser_Click(object sender, System.Windows.RoutedEventArgs e) { }
        private void PlanVisualTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e) { }
        private void RightPanel_Expanded(object sender, System.Windows.RoutedEventArgs e) { }
        private void RightPanel_Collapsed(object sender, System.Windows.RoutedEventArgs e) { }

        private void PlanNodifyGraph_NodeSelected(object? sender, PlanNodeViewModel? node)
        {
            PlanPropertiesGrid.ItemsSource = null;
            if (node?.RawElement == null) return;
            try
            {
                PlanPropertiesGrid.ItemsSource = new PlanPropertyService().BuildProperties(node.RawElement);
            }
            catch (Exception exception)
            {
                Logger.LogException("PlanView.NodeSelected", exception);
            }
        }

        private void PlanNodifyGraph_NodeDoubleClicked(object? sender, PlanNodeViewModel? node) =>
            PlanNodifyGraph_NodeSelected(sender, node);
    }
}
