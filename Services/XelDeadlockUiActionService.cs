using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class XelDeadlockUiActionService
    {
        private readonly Core.XelReader _xelReader;
        private readonly Core.Services.AnalysisSessionCoordinator _analysisSessions;
        private readonly ComboBox _selector;
        private readonly TabControl _mainTabControl;
        private readonly Func<string, string, string, Task> _analyzeDeadlockXmlAsync;
        private string _sourceFilePath = string.Empty;
        public InputRecognitionResult? CurrentInput { get; private set; }

        public XelDeadlockUiActionService(
            Core.XelReader xelReader,
            Core.Services.AnalysisSessionCoordinator analysisSessions,
            ComboBox selector,
            TabControl mainTabControl,
            Func<string, string, string, Task> analyzeDeadlockXmlAsync)
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
            ClearEvents();
            try
            {
                var input = await _xelReader.ReadDocumentAsync(filePath, cancellationToken: session.Token);
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                if (input.Status == InputStatus.Cancelled) return;
                if (!input.HasUsableContent)
                {
                    MessageBox.Show(
                        InputReadPresentation.Describe(input),
                        "XEL 读取失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                if (input.Status == InputStatus.Partial)
                    MessageBox.Show(InputReadPresentation.Describe(input), "部分读取", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowDocumentEvents(input, filePath);
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
            try
            {
                if (_selector.SelectedItem is DeadlockInput input)
                    await _analyzeDeadlockXmlAsync(input.Document.ToString(), _sourceFilePath, $"{_sourceFilePath} · {input.DisplayName}");
                else if (_selector.SelectedItem is Core.XelDeadlockReport report)
                    await _analyzeDeadlockXmlAsync(report.DeadlockXml, _sourceFilePath, $"XEL deadlock event {report.Timestamp}");
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

        public void ShowDocumentEvents(InputRecognitionResult input, string source)
        {
            if (!input.HasUsableContent) { ClearEvents(); return; }
            ShowXmlEvents(input.Deadlocks, source);
            CurrentInput = input;
            _selector.ToolTip = input.Status == InputStatus.Partial ? InputReadPresentation.Describe(input) : null;
        }

        public void ShowXmlEvents(IReadOnlyList<DeadlockInput> events, string source)
        {
            ClearEvents();
            _sourceFilePath = source;
            _selector.DisplayMemberPath = nameof(DeadlockInput.DisplayName);
            _selector.ItemsSource = events;
            _selector.Visibility = Visibility.Visible;
            _mainTabControl.SelectedIndex = 0;
            _selector.SelectedIndex = 0;
        }

        public void ClearEvents()
        {
            CurrentInput = null;
            _selector.ToolTip = null;
            _selector.ItemsSource = null;
            _selector.Visibility = Visibility.Collapsed;
            _sourceFilePath = string.Empty;
        }
    }
}
