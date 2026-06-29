using System;
using System.Windows;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanObfuscationExportUiActionService
    {
        private readonly Core.Services.IFileDialogService _fileDialogService;

        public PlanObfuscationExportUiActionService(
            Core.Services.IFileDialogService fileDialogService)
        {
            _fileDialogService = fileDialogService
                ?? throw new ArgumentNullException(nameof(fileDialogService));
        }

        public void Export(
            XDocument? currentPlanDocument,
            Action<string> setStatus)
        {
            ArgumentNullException.ThrowIfNull(setStatus);

            if (currentPlanDocument == null)
            {
                MessageBox.Show("Please load an execution plan first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? fileName = _fileDialogService.ShowSaveFile(
                new Core.Services.FileDialogRequest(
                    "Execution plan files (*.sqlplan)|*.sqlplan|XML files (*.xml)|*.xml",
                    "Export obfuscated execution plan",
                    ".sqlplan",
                    "Obfuscated_Plan.sqlplan"));

            if (fileName == null)
            {
                return;
            }

            try
            {
                setStatus("Generating obfuscated plan...");
                XDocument maskedDocument =
                    Core.Services.PlanObfuscatorService.ObfuscatePlan(currentPlanDocument);
                maskedDocument.Save(fileName);
                MessageBox.Show($"Obfuscated execution plan saved to:\n{fileName}\n\nSensitive table names and SQL text have been replaced, and the file remains readable by SSMS.", "Export succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
                setStatus("Ready");
            }
            catch (Exception ex)
            {
                Logger.LogException("ExportObfuscatedPlan_Click", ex);
                MessageBox.Show($"Export failed:\n{ex.Message}", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                setStatus("Obfuscated export failed");
            }
        }
    }
}
