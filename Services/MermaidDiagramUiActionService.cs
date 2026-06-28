using System;
using System.Windows;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class MermaidDiagramUiActionService
    {
        private readonly Core.Services.MermaidDiagramActionService _actionService;
        private readonly Core.Services.BrowserLauncher _browserLauncher;

        public MermaidDiagramUiActionService(
            Core.Services.MermaidDiagramActionService actionService,
            Core.Services.BrowserLauncher browserLauncher)
        {
            _actionService = actionService
                ?? throw new ArgumentNullException(nameof(actionService));
            _browserLauncher = browserLauncher
                ?? throw new ArgumentNullException(nameof(browserLauncher));
        }

        public void CopyDeadlockDiagram(XDocument? document)
        {
            CopyDiagram(
                () => _actionService.BuildDeadlockDiagram(document),
                "死锁 Mermaid 代码已成功复制到剪贴板！",
                "CopyDeadlockMermaid");
        }

        public void CopyPlanDiagram(
            XDocument? document,
            XNamespace showplanNamespace)
        {
            CopyDiagram(
                () => _actionService.BuildPlanDiagram(document, showplanNamespace),
                "执行计划 Mermaid 代码已成功复制到剪贴板！",
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
                MessageBox.Show(successMessage, "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
                Logger.Info($"{result.LogMessage} 已成功复制到剪贴板。");
            }
            catch (Exception ex)
            {
                Logger.LogException(logScope, ex);
                MessageBox.Show($"复制 Mermaid 代码失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Logger.Info($"{result.LogMessage} 已在浏览器中打开。");
            }
            catch (Exception ex)
            {
                Logger.LogException(logScope, ex);
                MessageBox.Show($"在浏览器中打开 Mermaid 图形失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool ShowMissingDocumentIfNeeded(
            Core.Services.MermaidDiagramActionResult result)
        {
            if (result.Status != Core.Services.MermaidDiagramActionStatus.MissingDocument)
            {
                return false;
            }

            MessageBox.Show(result.UserMessage, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }
    }
}
