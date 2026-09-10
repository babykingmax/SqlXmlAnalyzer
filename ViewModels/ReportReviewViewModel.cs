using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.ViewModels;

public sealed class ReportReviewViewModel : ObservableObject
{
    private readonly DiagnosticReport _source;
    private readonly DiagnosticReportExportService _exporter;
    private bool _redacted = true, _busy;
    private string? _rawPreview, _redactedPreview;
    private string _format;
    private string _status = "尚未导出。预览固定为打开窗口时的选择；重新选择后请重新打开报告。";
    public ReportReviewViewModel(DiagnosticReport source, string format = "html", IUnexpectedErrorReporter? unexpectedErrors = null,
        IDiagnosticReportFileWriter? writer = null)
    {
        _source = source; _format = format;
        Redaction = new ReportRedactionService(unexpectedErrors).Preview(source);
        _exporter = new(writer, unexpectedErrors);
    }
    public string[] Formats => _source.Kind == "ExecutionPlan" ? ["html", "pdf", "docx", "json", "svg", "sqlplan"] : ["html", "pdf", "docx", "json", "svg"];
    public ReportRedactionPreview Redaction { get; }
    public DiagnosticReport? Report => IsRedacted ? Redaction.Report : _source;
    public bool IsRedacted { get => _redacted; set { if (_busy) return; _redacted = value; NotifyPreview(); } }
    public string Format { get => _format; set { if (_busy || !Formats.Contains(value)) return; _format = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormatNotice)); } }
    public string FormatNotice => Format is "svg" or "sqlplan"
        ? "此格式仅导出静态图（最多 80 个节点）或选择范围 XML，不包含完整事实与诊断；完整报告请选择 HTML/PDF/Word/JSON。"
        : "HTML/PDF/Word/JSON 共用以下快照，事实、问题数、RuleId 和定位相同。";
    public string Scope => _source.Kind + " / " + _source.Scope;
    public string Counts => $"事实 {_source.FactCount}；问题 {_source.IssueCount}；Hit {_source.HitCount} / NoHit {_source.NoHitCount} / Skipped {_source.SkippedCount} / Failed {_source.FailedCount}";
    public string PreviewText => Report == null ? "脱敏预览不可用；已阻止导出。"
        : IsRedacted ? _redactedPreview ??= DiagnosticReportRenderer.Text(Report)
        : _rawPreview ??= DiagnosticReportRenderer.Text(Report);
    public string PrivacyNotice => Report?.PrivacyNotice ?? Redaction.Summary;
    public string Status => _status;
    public bool CanExport => !_busy && Report != null;
    public bool CanEdit => !_busy;
    public bool IsBusy => _busy;
    public async Task ExportAsync(string path, CancellationToken token = default)
    {
        var snapshot = Report;
        if (!CanExport || snapshot == null) return;
        string format = Format;
        _busy = true; _status = "正在导出；取消或失败不会发布不完整报告。"; NotifyPreview();
        try
        {
            var result = await Task.Run(() => _exporter.Export(snapshot, format, path, token));
            _status = result.Message;
        }
        finally { _busy = false; NotifyPreview(); }
    }
    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(IsRedacted)); OnPropertyChanged(nameof(Report)); OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(PrivacyNotice)); OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(Status));
    }
}
