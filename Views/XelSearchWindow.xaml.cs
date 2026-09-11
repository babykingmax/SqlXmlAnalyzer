using System.Windows;
using Microsoft.Win32;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Views;

public partial class XelSearchWindow : Window
{
    private XelSearchViewModel Model => (XelSearchViewModel)DataContext;
    public XelSearchWindow(XelSearchViewModel model)
    {
        InitializeComponent(); DataContext = model;
        Services.WorkspaceAccessibility.PrepareDialog(this);
        Closed += (_, _) => model.Dispose();
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Filter = "Extended Events (*.xel)|*.xel", Multiselect = true };
            if (dialog.ShowDialog(this) == true) await Model.ImportAsync(dialog.FileNames);
        }
        catch (Exception exception) { MessageBox.Show(this, ExceptionPolicy.Describe(exception, "IMP23.XelSearch.FileDialog"), "XEL 导入失败"); }
    }
    private async void Search_Click(object sender, RoutedEventArgs e) => await Model.SearchAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Model.Cancel();
    private void AllEvents_Click(object sender, RoutedEventArgs e) => Model.SelectedGroup = null;
    private async void Navigate_Click(object sender, RoutedEventArgs e) => await Model.NavigateAsync();
}
