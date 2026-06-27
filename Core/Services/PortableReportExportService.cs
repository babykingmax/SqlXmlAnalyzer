using System;
using System.Windows;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PortableReportExportRequest(
        string Extension,
        string Filter,
        PortableAnalysisReport Report,
        FrameworkElement? ImageElement);

    public enum PortableReportExportStatus
    {
        Exported,
        Cancelled
    }

    public sealed record PortableReportExportResult(
        PortableReportExportStatus Status,
        string? OutputPath);

    public sealed class PortableReportExportService
    {
        private readonly IPdfWordReportExporter _reportExporter;
        private readonly IFileDialogService _fileDialogService;

        public PortableReportExportService(
            IPdfWordReportExporter reportExporter,
            IFileDialogService fileDialogService)
        {
            _reportExporter = reportExporter
                ?? throw new ArgumentNullException(nameof(reportExporter));
            _fileDialogService = fileDialogService
                ?? throw new ArgumentNullException(nameof(fileDialogService));
        }

        public PortableReportExportResult Export(PortableReportExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Report);

            string extension = NormalizeExtension(request.Extension);
            string? outputPath = _fileDialogService.ShowSaveFile(
                new FileDialogRequest(
                    request.Filter,
                    $"Save {extension.ToUpperInvariant()} analysis report",
                    $".{extension}",
                    request.Report.DefaultFileName));

            if (outputPath == null)
            {
                return new PortableReportExportResult(
                    PortableReportExportStatus.Cancelled,
                    OutputPath: null);
            }

            _reportExporter.Export(
                extension,
                outputPath,
                request.Report.Title,
                request.Report.Content,
                request.ImageElement);

            return new PortableReportExportResult(
                PortableReportExportStatus.Exported,
                outputPath);
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                throw new ArgumentException(
                    "Report extension cannot be empty.",
                    nameof(extension));
            }

            return extension.Trim().TrimStart('.').ToLowerInvariant();
        }
    }
}
