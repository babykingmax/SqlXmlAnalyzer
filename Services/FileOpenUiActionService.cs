using System;
using System.Threading.Tasks;
using System.Windows;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class FileOpenUiActionService
    {
        private readonly Core.Services.IFileDialogService _fileDialogService;
        private readonly DocumentOpenService _documentOpenService;

        public FileOpenUiActionService(
            Core.Services.IFileDialogService fileDialogService,
            DocumentOpenService documentOpenService)
        {
            _fileDialogService = fileDialogService
                ?? throw new ArgumentNullException(nameof(fileDialogService));
            _documentOpenService = documentOpenService
                ?? throw new ArgumentNullException(nameof(documentOpenService));
        }

        public async Task OpenDeadlockAsync(
            Func<string, Task> analyzeXelFileAsync,
            Action<string> analyzeDeadlockFile)
        {
            string? fileName = _fileDialogService.ShowOpenFile(
                new Core.Services.FileDialogRequest(
                    "Deadlock files (*.xml;*.xdl;*.xel)|*.xml;*.xdl;*.xel|All files (*.*)|*.*",
                    "Open deadlock report"));

            if (fileName != null)
            {
                await AnalyzeDeadlockPathAsync(fileName, analyzeXelFileAsync, analyzeDeadlockFile);
            }
        }

        public void OpenPlan(Action<string> analyzeExecutionPlanFile)
        {
            string? fileName = _fileDialogService.ShowOpenFile(
                new Core.Services.FileDialogRequest(
                    "Execution plan files (*.sqlplan;*.xml)|*.sqlplan;*.xml|All files (*.*)|*.*",
                    "Open execution plan"));

            if (fileName != null)
            {
                analyzeExecutionPlanFile(fileName);
            }
        }

        public void HandleDragEnter(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
        }

        public async Task HandleDropAsync(
            DragEventArgs e,
            Func<string, Task> analyzeXelFileAsync,
            Action<string> analyzeDeadlockFile,
            Action<string> analyzeExecutionPlanFile)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0)
            {
                return;
            }

            Core.Services.DocumentDropActionResult action =
                _documentOpenService.BuildDropAction(files[0]);

            switch (action.Status)
            {
                case Core.Services.DocumentDropActionStatus.Empty:
                    return;
                case Core.Services.DocumentDropActionStatus.Unsupported:
                    MessageBox.Show(
                        action.UserMessage,
                        "Unsupported file",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
            }

            switch (action.Kind)
            {
                case AnalysisDocumentKind.DeadlockXml:
                    analyzeDeadlockFile(action.FilePath);
                    break;
                case AnalysisDocumentKind.XelDeadlockTrace:
                    await analyzeXelFileAsync(action.FilePath);
                    break;
                case AnalysisDocumentKind.ExecutionPlanXml:
                    analyzeExecutionPlanFile(action.FilePath);
                    break;
            }
        }

        private async Task AnalyzeDeadlockPathAsync(
            string fileName,
            Func<string, Task> analyzeXelFileAsync,
            Action<string> analyzeDeadlockFile)
        {
            AnalysisDocumentKind kind = _documentOpenService.ClassifyDeadlockOpenPath(fileName);
            if (kind == AnalysisDocumentKind.XelDeadlockTrace)
            {
                await analyzeXelFileAsync(fileName);
            }
            else
            {
                analyzeDeadlockFile(fileName);
            }
        }
    }
}
