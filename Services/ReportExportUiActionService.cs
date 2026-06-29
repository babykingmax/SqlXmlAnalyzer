using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class ReportExportUiActionService
    {
        private readonly Core.Services.HtmlReportActionService _htmlReportActionService;
        private readonly Core.Services.HtmlReportExportService _htmlReportExportService;
        private readonly Core.Services.PortableReportActionService _portableReportActionService;
        private readonly Core.Services.PortableReportExportService _portableReportExportService;
        private readonly Core.Services.BrowserLauncher _browserLauncher;

        public ReportExportUiActionService(
            Core.Services.HtmlReportActionService htmlReportActionService,
            Core.Services.HtmlReportExportService htmlReportExportService,
            Core.Services.PortableReportActionService portableReportActionService,
            Core.Services.PortableReportExportService portableReportExportService,
            Core.Services.BrowserLauncher browserLauncher)
        {
            _htmlReportActionService = htmlReportActionService
                ?? throw new ArgumentNullException(nameof(htmlReportActionService));
            _htmlReportExportService = htmlReportExportService
                ?? throw new ArgumentNullException(nameof(htmlReportExportService));
            _portableReportActionService = portableReportActionService
                ?? throw new ArgumentNullException(nameof(portableReportActionService));
            _portableReportExportService = portableReportExportService
                ?? throw new ArgumentNullException(nameof(portableReportExportService));
            _browserLauncher = browserLauncher
                ?? throw new ArgumentNullException(nameof(browserLauncher));
        }

        public void GenerateHtmlReport(
            int selectedTabIndex,
            XDocument? currentDeadlockDocument,
            string? currentDeadlockFilePath,
            string deadlockDetailText,
            XDocument? currentPlanDocument,
            string? currentPlanFilePath,
            XNamespace showplanNamespace,
            ObservableCollection<MissingIndexSuggestion> missingIndexes)
        {
            try
            {
                Core.Services.HtmlReportActionResult action =
                    _htmlReportActionService.BuildReport(
                        selectedTabIndex,
                        currentDeadlockDocument,
                        currentDeadlockFilePath,
                        deadlockDetailText,
                        currentPlanDocument,
                        currentPlanFilePath,
                        showplanNamespace);

                if (action.Status != Core.Services.HtmlReportActionStatus.Ready
                    || action.Report == null)
                {
                    MessageBox.Show(action.UserMessage, "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Core.Services.HtmlAnalysisReport report = action.Report;
                if (selectedTabIndex == 1)
                {
                    missingIndexes.Clear();
                    foreach (MissingIndexSuggestion missingIndex in report.MissingIndexes)
                    {
                        missingIndexes.Add(missingIndex);
                    }
                }

                Core.Services.HtmlReportExportResult result =
                    _htmlReportExportService.Export(
                        new Core.Services.HtmlReportExportRequest(report));

                if (result.Status == Core.Services.HtmlReportExportStatus.Exported
                    && result.OutputPath != null)
                {
                    Logger.Info($"{report.AnalysisType} HTML report saved to {result.OutputPath}");

                    if (MessageBox.Show("Report saved successfully. Open it now?", "Save succeeded", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _browserLauncher.OpenFile(result.OutputPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("GenerateHtmlReport_Click", ex);
                MessageBox.Show($"HTML report generation failed: {ex.Message}\n\nDetails were written to the log.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ExportPortableReport(
            int selectedTabIndex,
            string? currentDeadlockFilePath,
            IEnumerable<DeadlockPattern>? deadlockPatterns,
            string deadlockDetailText,
            string? currentPlanFilePath,
            string planDiagnosticsText,
            FrameworkElement deadlockDiagramElement,
            string extension,
            string filter)
        {
            try
            {
                Core.Services.PortableReportActionResult action =
                    _portableReportActionService.BuildReport(
                        selectedTabIndex,
                        currentDeadlockFilePath,
                        deadlockPatterns,
                        deadlockDetailText,
                        currentPlanFilePath,
                        planDiagnosticsText,
                        extension);

                if (action.Status == Core.Services.PortableReportActionStatus.MissingContent)
                {
                    MessageBox.Show(action.UserMessage, "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (action.Status != Core.Services.PortableReportActionStatus.Ready
                    || action.Report == null)
                {
                    return;
                }

                FrameworkElement? imageElement =
                    action.IncludeDeadlockDiagram ? deadlockDiagramElement : null;

                Core.Services.PortableReportExportResult result =
                    _portableReportExportService.Export(
                        new Core.Services.PortableReportExportRequest(
                            extension,
                            filter,
                            action.Report,
                            imageElement));

                if (result.Status == Core.Services.PortableReportExportStatus.Exported)
                {
                    MessageBox.Show($"{extension.ToUpperInvariant()} report exported successfully.", "Export succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException($"ExportTo{extension.ToUpperInvariant()}_Click", ex);
                MessageBox.Show($"Export failed:\n{ex.Message}", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
