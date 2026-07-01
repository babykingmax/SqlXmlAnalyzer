using System;
using System.Windows;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class MermaidDiagramUiActionService
    {
        private readonly Core.Services.MermaidDiagramActionService _actionService;
        private readonly Core.Services.BrowserLauncher _browserLauncher;
        private readonly Core.ViewModels.MainViewModel _viewModel;
        private readonly XNamespace _showplanNamespace;

        public MermaidDiagramUiActionService(
            Core.Services.MermaidDiagramActionService actionService,
            Core.Services.BrowserLauncher browserLauncher,
            Core.ViewModels.MainViewModel viewModel,
            XNamespace showplanNamespace)
        {
            _actionService = actionService
                ?? throw new ArgumentNullException(nameof(actionService));
            _browserLauncher = browserLauncher
                ?? throw new ArgumentNullException(nameof(browserLauncher));
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
            _showplanNamespace = showplanNamespace
                ?? throw new ArgumentNullException(nameof(showplanNamespace));
        }

        public void CopyDeadlockDiagram()
        {
            CopyDeadlockDiagram(_viewModel.CurrentDeadlockDoc);
        }

        public void CopyPlanDiagram()
        {
            CopyPlanDiagram(_viewModel.CurrentPlanDoc, _showplanNamespace);
        }

        public void OpenPlanDiagram()
        {
            OpenPlanDiagram(_viewModel.CurrentPlanDoc, _showplanNamespace);
        }

        public void OpenDeadlockDiagram()
        {
            OpenDeadlockDiagram(_viewModel.CurrentDeadlockDoc);
        }

        public void CopyDeadlockDiagram(XDocument? document)
        {
            CopyDiagram(
                () => _actionService.BuildDeadlockDiagram(document),
                "Deadlock Mermaid code copied to clipboard.",
                "CopyDeadlockMermaid");
        }

        public void CopyPlanDiagram(
            XDocument? document,
            XNamespace showplanNamespace)
        {
            CopyDiagram(
                () => _actionService.BuildPlanDiagram(document, showplanNamespace),
                "Execution plan Mermaid code copied to clipboard.",
                "CopyPlanMermaid");
        }

        public void OpenPlanDiagram(
            XDocument? document,
            XNamespace showplanNamespace)
        {
            OpenDiagram(
                () => _actionService.BuildPlanDiagram(document, showplanNamespace),
                "OpenPlanMermaidInBrowser");
        }

        public void OpenDeadlockDiagram(XDocument? document)
        {
            OpenDiagram(
                () => _actionService.BuildDeadlockDiagram(document),
                "OpenDeadlockMermaidInBrowser");
        }

        private void CopyDiagram(
            Func<Core.Services.MermaidDiagramActionResult> buildResult,
            string successMessage,
            string logScope)
        {
            try
            {
                Core.Services.MermaidDiagramActionResult result = buildResult();

                if (ShowMissingDocumentIfNeeded(result))
                {
                    return;
                }

                Clipboard.SetText(result.MermaidCode);
                MessageBox.Show(successMessage, "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                Logger.Info($"{result.LogMessage} Copied to clipboard.");
            }
            catch (Exception ex)
            {
                Logger.LogException(logScope, ex);
                MessageBox.Show($"Failed to copy Mermaid code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDiagram(
            Func<Core.Services.MermaidDiagramActionResult> buildResult,
            string logScope)
        {
            try
            {
                Core.Services.MermaidDiagramActionResult result = buildResult();

                if (ShowMissingDocumentIfNeeded(result))
                {
                    return;
                }

                _browserLauncher.OpenMermaid(result.MermaidCode);
                Logger.Info($"{result.LogMessage} Opened in browser.");
            }
            catch (Exception ex)
            {
                Logger.LogException(logScope, ex);
                MessageBox.Show($"Failed to open the Mermaid diagram in a browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool ShowMissingDocumentIfNeeded(
            Core.Services.MermaidDiagramActionResult result)
        {
            if (result.Status != Core.Services.MermaidDiagramActionStatus.MissingDocument)
            {
                return false;
            }

            MessageBox.Show(result.UserMessage, "Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }
    }
}
