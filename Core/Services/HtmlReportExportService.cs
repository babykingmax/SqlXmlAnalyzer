using System;

namespace SqlXmlAnalyzer.Core.Services
{
    public interface IHtmlReportWriter
    {
        string Save(HtmlAnalysisReport report, string outputPath);
    }

    public sealed class HtmlReportWriter : IHtmlReportWriter
    {
        public string Save(HtmlAnalysisReport report, string outputPath)
        {
            ArgumentNullException.ThrowIfNull(report);

            return HtmlReportGenerator.SaveReport(
                report.OriginalFilePath,
                report.AnalysisType,
                report.SummaryText,
                report.MermaidCode,
                report.Sections,
                outputPath);
        }
    }

    public sealed record HtmlReportExportRequest(
        HtmlAnalysisReport Report);

    public enum HtmlReportExportStatus
    {
        Exported,
        Cancelled
    }

    public sealed record HtmlReportExportResult(
        HtmlReportExportStatus Status,
        string? OutputPath);

    public sealed class HtmlReportExportService
    {
        private readonly IHtmlReportWriter _reportWriter;
        private readonly IFileDialogService _fileDialogService;

        public HtmlReportExportService(
            IHtmlReportWriter reportWriter,
            IFileDialogService fileDialogService)
        {
            _reportWriter = reportWriter
                ?? throw new ArgumentNullException(nameof(reportWriter));
            _fileDialogService = fileDialogService
                ?? throw new ArgumentNullException(nameof(fileDialogService));
        }

        public HtmlReportExportResult Export(HtmlReportExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Report);

            HtmlAnalysisReport report = request.Report;
            string? outputPath = _fileDialogService.ShowSaveFile(
                new FileDialogRequest(
                    "HTML report (*.html)|*.html",
                    $"Save {report.AnalysisType} analysis report",
                    ".html",
                    report.DefaultFileName));

            if (outputPath == null)
            {
                return new HtmlReportExportResult(
                    HtmlReportExportStatus.Cancelled,
                    OutputPath: null);
            }

            string savedPath = _reportWriter.Save(report, outputPath);
            return new HtmlReportExportResult(
                HtmlReportExportStatus.Exported,
                savedPath);
        }
    }
}
