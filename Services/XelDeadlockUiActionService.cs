using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class XelDeadlockUiActionService
    {
        private readonly Core.XelReader _xelReader;
        private readonly Core.Services.AnalysisSessionCoordinator _analysisSessions;
        private readonly ComboBox _selector;
        private readonly TabControl _mainTabControl;
        private readonly Func<string, string, Task> _analyzeDeadlockXmlAsync;

        public XelDeadlockUiActionService(
            Core.XelReader xelReader,
            Core.Services.AnalysisSessionCoordinator analysisSessions,
            ComboBox selector,
            TabControl mainTabControl,
            Func<string, string, Task> analyzeDeadlockXmlAsync)
        {
            _xelReader = xelReader
                ?? throw new ArgumentNullException(nameof(xelReader));
            _analysisSessions = analysisSessions
                ?? throw new ArgumentNullException(nameof(analysisSessions));
            _selector = selector
                ?? throw new ArgumentNullException(nameof(selector));
            _mainTabControl = mainTabControl
                ?? throw new ArgumentNullException(nameof(mainTabControl));
            _analyzeDeadlockXmlAsync = analyzeDeadlockXmlAsync
                ?? throw new ArgumentNullException(nameof(analyzeDeadlockXmlAsync));
        }

        public async Task AnalyzeXelFileAsync(string filePath)
        {
            Core.Services.AnalysisSession session = _analysisSessions.Begin();
            try
            {
                var reports = await _xelReader.ReadDeadlocksAsync(filePath, session.Token);
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                if (reports.Count == 0)
                {
                    MessageBox.Show(
                        "No xml_deadlock_report events were found in this XEL file.",
                        "No deadlocks found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _selector.ItemsSource = reports;
                _selector.Visibility = Visibility.Visible;
                _selector.SelectedIndex = 0;
                _mainTabControl.SelectedIndex = 0;
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"XEL analysis canceled: {filePath}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                Logger.LogException("MainWindow.AnalyzeXelFileAsync", ex);
                MessageBox.Show(
                    "Failed to parse the XEL file: " + ex.Message,
                    "XEL parse failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public async Task HandleSelectionChangedAsync()
        {
            if (_selector.SelectedItem is not Core.XelDeadlockReport report)
            {
                return;
            }

            try
            {
                await _analyzeDeadlockXmlAsync(
                    report.DeadlockXml,
                    $"XEL deadlock event {report.Timestamp}");
            }
            catch (Exception ex)
            {
                Logger.LogException("Render XEL deadlock graph failed (Selector_SelectionChanged)", ex);
                MessageBox.Show(
                    "Failed to render the selected deadlock graph: " + ex.Message,
                    "Deadlock render failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
