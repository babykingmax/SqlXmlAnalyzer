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
                    MessageBox.Show("该 XEL 文件中没有找到任何 xml_deadlock_report 事件。");
                    return;
                }

                _selector.ItemsSource = reports;
                _selector.Visibility = Visibility.Visible;
                _selector.SelectedIndex = 0;
                _mainTabControl.SelectedIndex = 0;
            }
            catch (OperationCanceledException)
            {
                Logger.Verbose($"XEL 分析已取消: {filePath}");
            }
            catch (Exception ex)
            {
                if (!_analysisSessions.IsCurrent(session.RequestId))
                {
                    return;
                }

                Logger.LogException("MainWindow.AnalyzeXelFileAsync", ex);
                MessageBox.Show("解析 XEL 文件时发生错误: " + ex.Message);
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
                    $"XEL 死锁事件 {report.Timestamp}");
            }
            catch (Exception ex)
            {
                Logger.LogException("渲染死锁图失败 (Selector_SelectionChanged)", ex);
                MessageBox.Show("渲染死锁图失败: " + ex.Message);
            }
        }
    }
}
