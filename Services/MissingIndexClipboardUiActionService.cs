using System;
using System.Windows;
using System.Windows.Controls;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class MissingIndexClipboardUiActionService
    {
        private readonly Core.Services.MissingIndexClipboardActionService _clipboardActionService;

        public MissingIndexClipboardUiActionService(
            Core.Services.MissingIndexClipboardActionService clipboardActionService)
        {
            _clipboardActionService = clipboardActionService
                ?? throw new ArgumentNullException(nameof(clipboardActionService));
        }

        public void CopyCreateScript(object sender)
        {
            CopyResult(
                _clipboardActionService.BuildCreateScript(
                    sender is Button button ? button.Tag as string : null));
        }

        public void CopyRollbackScript(object sender)
        {
            CopyResult(
                _clipboardActionService.BuildRollbackScript(
                    sender is Button button ? button.Tag as string : null));
        }

        public void CopyDeploymentBundle(object sender)
        {
            CopyResult(
                _clipboardActionService.BuildDeploymentBundle(
                    sender is Button button
                        ? button.DataContext as MissingIndexSuggestion
                        : null));
        }

        private static void CopyResult(Core.Services.MissingIndexClipboardActionResult result)
        {
            if (result.Status == Core.Services.MissingIndexClipboardActionStatus.Ready)
            {
                Clipboard.SetText(result.Text);
                MessageBox.Show(result.SuccessMessage, "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
