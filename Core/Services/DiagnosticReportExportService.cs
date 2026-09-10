using System.IO;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Reporting;
using Xceed.Words.NET;

namespace SqlXmlAnalyzer.Core.Services;

public interface IDiagnosticReportFileWriter
{
    void Write(DiagnosticReport report, string format, string path, CancellationToken token);
}

public sealed class DiagnosticReportFileWriter : IDiagnosticReportFileWriter
{
    public void Write(DiagnosticReport report, string format, string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Render text only for formats that consume it; all model/renderer budgets are enforced upstream.
        switch (format)
        {
            case "html": File.WriteAllText(path, DiagnosticReportRenderer.Html(report, token), new UTF8Encoding(false, true)); break;
            case "json": File.WriteAllText(path, DiagnosticReportRenderer.Json(report, token), new UTF8Encoding(false, true)); break;
            case "svg": File.WriteAllText(path, DiagnosticReportRenderer.Svg(report, token), new UTF8Encoding(false, true)); break;
            case "sqlplan":
                if (report.Kind != "ExecutionPlan") throw new InvalidDataException("仅执行计划支持 .sqlplan 输出。");
                File.WriteAllText(path, report.SourceXml, new UTF8Encoding(false, true)); break;
            case "pdf":
                string pdfText = DiagnosticReportRenderer.Text(report, token);
                QuestPDF.Settings.License = LicenseType.Community;
                Document.Create(document => document.Page(page =>
                {
                    page.Size(PageSizes.A4); page.Margin(30); page.DefaultTextStyle(s => s.FontSize(9).FontFamily("Microsoft YaHei"));
                    page.Header().Text("DiagnosticReport / " + report.Scope).Bold().FontSize(16);
                    page.Content().Column(column =>
                    {
                        if (report.Nodes.Count > 0) column.Item().Height(Math.Min(220, Math.Max(60, report.Nodes.Count * 45 + 25))).Svg(DiagnosticReportRenderer.Svg(report, token)).FitArea();
                        // Separate paragraphs allow page breaking even for large source XML.
                        foreach (string line in pdfText.Split('\n')) { token.ThrowIfCancellationRequested(); column.Item().Text(line.TrimEnd('\r')); }
                    });
                    page.Footer().AlignCenter().Text(t => { t.Span("Page "); t.CurrentPageNumber(); });
                })).GeneratePdf(path);
                break;
            case "docx":
                string wordText = DiagnosticReportRenderer.Text(report, token);
                QuestPDF.Settings.License = LicenseType.Community;
                using (var document = DocX.Create(path))
                {
                    document.InsertParagraph("DiagnosticReport / " + report.Scope).Bold().FontSize(16);
                    if (report.Nodes.Count > 0)
                    {
                        int graphHeight = Math.Min(440, Math.Max(100, report.Nodes.Count * 70 + 48));
                        byte[] png = Document.Create(d => d.Page(p => { p.Size(900, graphHeight); p.Margin(0); p.Content().Svg(DiagnosticReportRenderer.Svg(report, token)).FitArea(); }))
                            .GenerateImages().Single();
                        using var image = new MemoryStream(png);
                        document.InsertParagraph().AppendPicture(document.AddImage(image).CreatePicture(graphHeight / 2, 450));
                    }
                    foreach (string line in wordText.Split('\n')) { token.ThrowIfCancellationRequested(); document.InsertParagraph(line.TrimEnd('\r')).Font("Microsoft YaHei").FontSize(9); }
                    document.Save();
                }
                break;
            default: throw new InvalidDataException("不支持的报告格式。");
        }
        token.ThrowIfCancellationRequested();
    }
}

public sealed record DiagnosticReportExportResult(bool Succeeded, string Message);
public sealed class DiagnosticReportExportService(IDiagnosticReportFileWriter? writer = null, IUnexpectedErrorReporter? unexpectedErrors = null)
{
    public DiagnosticReportExportResult Export(DiagnosticReport report, string format, string path, CancellationToken token = default)
    {
        string? temporary = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (format is not ("html" or "json" or "pdf" or "docx" or "svg" or "sqlplan")) throw new InvalidDataException("不支持的报告格式。");
            string destination;
            try
            {
                if (!string.Equals(Path.GetExtension(path), "." + format, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件扩展名与报告格式不一致。");
                destination = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            { throw new InvalidDataException("报告输出路径无效。", exception); }
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("请使用新的文件名；报告不会覆盖既有文件。");
            string candidate = Path.Combine(Path.GetDirectoryName(destination)!, ".tmp.report-" + Guid.NewGuid().ToString("N") + "." + format);
            using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            temporary = candidate;
            Logger.Debug("IMP-22: report export started.");
            (writer ?? new DiagnosticReportFileWriter()).Write(report, format, temporary, token);
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (stream.Length == 0) throw new IOException("报告输出为空，未发布文件。");
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            temporary = null;
            Logger.Debug("IMP-22: report export completed.");
            return new(true, "新文件已完整保存。" + Environment.NewLine + report.PrivacyNotice);
        }
        catch (Exception exception)
        {
            return new(false, ExceptionPolicy.Describe(exception, "IMP22.ReportExport", unexpectedErrors));
        }
        finally
        {
            if (temporary != null)
                try { File.Delete(temporary); }
                catch (Exception exception) { ExceptionPolicy.Describe(exception, "IMP22.ReportExport.Cleanup", unexpectedErrors); }
        }
    }
}
