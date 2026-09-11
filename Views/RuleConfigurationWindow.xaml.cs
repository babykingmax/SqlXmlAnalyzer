using System.Windows;
using Microsoft.Win32;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Views;

public partial class RuleConfigurationWindow : Window
{
    public RuleConfigurationViewModel Model { get; }
    public RuleConfigurationWindow(RuleConfigurationViewModel model)
    {
            InitializeComponent(); DataContext = Model = model;
            Services.WorkspaceAccessibility.PrepareDialog(this);
        Closed += (_, _) => model.Dispose();
    }
    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Filter = "规则配置 (*.json)|*.json", Multiselect = false };
            if (dialog.ShowDialog(this) == true) await Model.LoadAsync(dialog.FileName);
        }
        catch (Exception exception) { ShowError(exception, "LoadDialog"); }
    }
    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog { Filter = "规则配置 (*.json)|*.json", DefaultExt = ".json", FileName = "RuleConfiguration.custom.json",
                OverwritePrompt = false };
            if (dialog.ShowDialog(this) == true) await Model.SaveAsAsync(dialog.FileName);
        }
        catch (Exception exception) { ShowError(exception, "SaveDialog"); }
    }
    private void ShowError(Exception exception, string operation) => MessageBox.Show(this,
        ExceptionPolicy.Describe(exception, "IMP24.Configuration." + operation), "规则配置操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
    private async void Save_Click(object sender, RoutedEventArgs e) => await Model.SaveAsync();
    private void Reset_Click(object sender, RoutedEventArgs e) => Model.RestoreDefaults();
    private void Apply_Click(object sender, RoutedEventArgs e) => Model.Apply();
    private async void Recalculate_Click(object sender, RoutedEventArgs e) => await Model.RecalculateAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Model.Cancel();
}
