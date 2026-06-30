using System;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class MainWindowShellActionService
    {
        private readonly Core.Services.AnalysisClipboardService _clipboardService;
        private readonly Core.Services.LogFolderActionService _logFolderActionService;
        private readonly Core.Services.BrowserLauncher _browserLauncher;
        private readonly Core.Services.FileAssociationRegistrationService _fileAssociationRegistrationService;
        private readonly Window _owner;
        private readonly TabControl _mainTabControl;
        private readonly Core.ViewModels.MainViewModel _viewModel;
        private readonly Func<bool> _useDarkThemeProvider;

        public MainWindowShellActionService(
            Core.Services.AnalysisClipboardService clipboardService,
            Core.Services.LogFolderActionService logFolderActionService,
            Core.Services.BrowserLauncher browserLauncher,
            Core.Services.FileAssociationRegistrationService fileAssociationRegistrationService,
            Window owner,
            TabControl mainTabControl,
            Core.ViewModels.MainViewModel viewModel,
            Func<bool> useDarkThemeProvider)
        {
            _clipboardService = clipboardService
                ?? throw new ArgumentNullException(nameof(clipboardService));
            _logFolderActionService = logFolderActionService
                ?? throw new ArgumentNullException(nameof(logFolderActionService));
            _browserLauncher = browserLauncher
                ?? throw new ArgumentNullException(nameof(browserLauncher));
            _fileAssociationRegistrationService = fileAssociationRegistrationService
                ?? throw new ArgumentNullException(nameof(fileAssociationRegistrationService));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _mainTabControl = mainTabControl
                ?? throw new ArgumentNullException(nameof(mainTabControl));
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
            _useDarkThemeProvider = useDarkThemeProvider
                ?? throw new ArgumentNullException(nameof(useDarkThemeProvider));
        }

        public void CopyAnalysisResult()
        {
            CopyAnalysisResult(
                _mainTabControl.SelectedIndex,
                _viewModel.DeadlockPatternText,
                _viewModel.PlanWarningsText);
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
                    MessageBox.Show(result.UserMessage, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (result.Status == Core.Services.AnalysisClipboardStatus.Ready)
                {
                    Clipboard.SetText(result.Text);
                    MessageBox.Show("Diagnostic results copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Copy failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void CopyRefactoredSql(string? refactoredSql)
        {
            Core.Services.AnalysisClipboardResult result =
                _clipboardService.BuildRefactoredSql(refactoredSql);

            if (result.Status == Core.Services.AnalysisClipboardStatus.Ready)
            {
                Clipboard.SetText(result.Text);
                MessageBox.Show("Refactored SQL copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (result.Status == Core.Services.AnalysisClipboardStatus.Empty)
            {
                MessageBox.Show(result.UserMessage, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public void OpenLogsFolder()
        {
            Core.Services.LogFolderActionResult result =
                _logFolderActionService.BuildOpenLogsFolder();

            if (result.Status == Core.Services.LogFolderActionStatus.MissingDirectory)
            {
                MessageBox.Show(result.UserMessage, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _browserLauncher.OpenFolder(result.FolderPath);
            }
            catch (Exception ex)
            {
                Logger.LogException("OpenLogsFolder_Click", ex);
                MessageBox.Show($"Unable to open the log folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ShowAboutAndRegisterAssociations()
        {
            MessageBoxResult result = MessageBox.Show(
                "SqlXmlAnalyzer v2.0\n\n" +
                "Highlights:\n" +
                "1. Execution plan visualization with smart graph collapse.\n" +
                "2. Deadlock playback with focused graph paths.\n" +
                "3. Missing-index tuning sandbox with tipping-point analysis.\n" +
                "4. Parameter-sensitivity comparison and updated diagram exports.\n\n" +
                "Register .sqlplan and .xdl file associations for the current user?",
                "About & File Associations",
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

        public void HandleTitleBarMouseLeftButtonDown(int clickCount)
        {
            HandleTitleBarMouseLeftButtonDown(_owner, clickCount);
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

        public void Minimize()
        {
            Minimize(_owner);
        }

        public void ToggleMaximize(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        public void ToggleMaximize()
        {
            ToggleMaximize(_owner);
        }

        public void Close(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            window.Close();
        }

        public void Close()
        {
            Close(_owner);
        }

        public void SetTheme()
        {
            SetTheme(_useDarkThemeProvider());
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
                    MessageBox.Show("File association registration failed: the current-user registry keys are not accessible.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show("File associations registered. You can now open .sqlplan and .xdl files directly.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"File association registration failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
