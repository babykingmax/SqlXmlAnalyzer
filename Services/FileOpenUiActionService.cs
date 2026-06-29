using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class FileOpenUiActionService
    {
        private readonly Core.Services.IFileDialogService _fileDialogService;

        public FileOpenUiActionService(Core.Services.IFileDialogService fileDialogService)
        {
            _fileDialogService = fileDialogService
                ?? throw new ArgumentNullException(nameof(fileDialogService));
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

            string file = files[0];
            string extension = Path.GetExtension(file).ToLowerInvariant();

            if (extension == ".xml" || extension == ".xdl")
            {
                analyzeDeadlockFile(file);
            }
            else if (extension == ".xel")
            {
                await analyzeXelFileAsync(file);
            }
            else if (extension == ".sqlplan")
            {
                analyzeExecutionPlanFile(file);
            }
            else
            {
                MessageBox.Show("不支持的文件类型，请选择死锁(XML/XEL)或执行计划(.sqlplan)文件。");
            }
        }

        private static async Task AnalyzeDeadlockPathAsync(
            string fileName,
            Func<string, Task> analyzeXelFileAsync,
            Action<string> analyzeDeadlockFile)
        {
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension == ".xel")
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
