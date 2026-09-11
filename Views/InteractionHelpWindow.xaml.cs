using System.Windows;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Views;

public partial class InteractionHelpWindow : Window
{
    private readonly Core.ViewModels.MainViewModel _model;
    public InteractionHelpWindow(Core.ViewModels.MainViewModel model)
    {
        _model = model; InitializeComponent(); ReduceMotion.IsChecked = model.ReduceMotion;
        WorkspaceAccessibility.PrepareDialog(this);
    }
    private void MotionChanged(object sender, RoutedEventArgs e) => _model.ReduceMotion = ReduceMotion.IsChecked == true;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
