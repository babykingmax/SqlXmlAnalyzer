using System;
using System.Windows;
using MaterialDesignThemes.Wpf;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class MainWindowShellActionService
    {
        private readonly Core.Services.AnalysisClipboardService _clipboardService;
        private readonly Core.Services.LogFolderActionService _logFolderActionService;
        private readonly Core.Services.BrowserLauncher _browserLauncher;
        private readonly Core.Services.FileAssociationRegistrationService _fileAssociationRegistrationService;

        public MainWindowShellActionService(
            Core.Services.AnalysisClipboardService clipboardService,
            Core.Services.LogFolderActionService logFolderActionService,
            Core.Services.BrowserLauncher browserLauncher,
            Core.Services.FileAssociationRegistrationService fileAssociationRegistrationService)
        {
            _clipboardService = clipboardService
                ?? throw new ArgumentNullException(nameof(clipboardService));
            _logFolderActionService = logFolderActionService
                ?? throw new ArgumentNullException(nameof(logFolderActionService));
            _browserLauncher = browserLauncher
                ?? throw new ArgumentNullException(nameof(browserLauncher));
            _fileAssociationRegistrationService = fileAssociationRegistrationService
                ?? throw new ArgumentNullException(nameof(fileAssociationRegistrationService));
        }

        public void CopyAnalysisResult(
            int selectedTabIndex,
            string? deadlockPatternText,
            string? planWarningsText)
        {
            try
            {
                Core.Services.AnalysisClipboardResult result =
                    _clipboardService.BuildForTab(
                        selectedTabIndex,
                        deadlockPatternText,
                        planWarningsText);

                if (result.Status == Core.Services.AnalysisClipboardStatus.Empty)
                {
                    MessageBox.Show(result.UserMessage, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (result.Status == Core.Services.AnalysisClipboardStatus.Ready)
                {
                    Clipboard.SetText(result.Text);
                    MessageBox.Show("诊断结果已成功复制到剪贴板！", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败:\n{ex.Message}", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void CopyRefactoredSql(string? refactoredSql)
        {
            Core.Services.AnalysisClipboardResult result =
                _clipboardService.BuildRefactoredSql(refactoredSql);

            if (result.Status == Core.Services.AnalysisClipboardStatus.Ready)
            {
                Clipboard.SetText(result.Text);
                MessageBox.Show("重构后的 SQL 已成功复制到剪贴板！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public void OpenLogsFolder()
        {
            Core.Services.LogFolderActionResult result =
                _logFolderActionService.BuildOpenLogsFolder();

            if (result.Status == Core.Services.LogFolderActionStatus.MissingDirectory)
            {
                MessageBox.Show(result.UserMessage);
                return;
            }

            try
            {
                _browserLauncher.OpenFolder(result.FolderPath);
            }
            catch (Exception ex)
            {
                Logger.LogException("OpenLogsFolder_Click", ex);
                MessageBox.Show($"无法打开日志文件夹: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ShowAboutAndRegisterAssociations()
        {
            MessageBoxResult result = MessageBox.Show(
                "SqlXmlAnalyzer 专业图形界面版 v2.0\n\n" +
                "功能特性：\n" +
                "1. 完美的执行计划可视化与智能折叠 (基于 Nodify)\n" +
                "2. 深度死锁回放与有向图关键路径聚焦\n" +
                "3. 索引调优沙盒与 Tipping Point 临界线分析\n" +
                "4. 参数嗅探并排对比与直方图绘制\n\n" +
                "是否关联 .sqlplan 与 .xdl 文件到系统右键菜单？\n" +
                "（点击“是”将为当前用户注册文件关联，“否”则仅关闭此窗口）",
                "关于 & 关联设置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                RegisterAssociations();
            }
        }

        public void ExitApplication()
        {
            global::System.Windows.Application.Current.Shutdown();
        }

        public void HandleTitleBarMouseLeftButtonDown(Window window, int clickCount)
        {
            ArgumentNullException.ThrowIfNull(window);

            if (clickCount == 2)
            {
                ToggleMaximize(window);
                return;
            }

            window.DragMove();
        }

        public void Minimize(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            window.WindowState = WindowState.Minimized;
        }

        public void ToggleMaximize(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        public void Close(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            window.Close();
        }

        public void SetTheme(bool useDarkTheme)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(useDarkTheme ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(theme);
        }

        private void RegisterAssociations()
        {
            try
            {
                Core.Services.FileAssociationRegistrationResult registrationResult =
                    _fileAssociationRegistrationService.RegisterCurrentUserAssociations();

                if (registrationResult.Status
                    != Core.Services.FileAssociationRegistrationStatus.Registered)
                {
                    MessageBox.Show("文件关联注册失败：无法访问当前用户的文件关联注册表。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show("文件关联注册成功！您现在可以直接双击或右键打开 .sqlplan 和 .xdl 文件了。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"注册文件关联失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
