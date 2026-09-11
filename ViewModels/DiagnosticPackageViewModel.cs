using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.ViewModels;

public sealed class DiagnosticPackageViewModel : ObservableObject
{
    private readonly DiagnosticReport? _report;
    private readonly RuleConfigurationDocument? _configuration;
    private readonly AnalysisProgress? _progress;
    private readonly DiagnosticPackageBuilder _builder;
    private readonly DiagnosticPackageExporter _exporter;
    private readonly IUnexpectedErrorReporter? _unexpectedErrors;
    private bool _includeReport, _includeRawSelection, _busy;
    private string? _logPath, _dumpPath;
    private string _status = "请选择内容并生成预览。当前工作区后续变化不会改变本窗口的报告快照。";
    private DiagnosticPackage? _package;
    private DiagnosticPackageEntry? _selectedEntry;

    public DiagnosticPackageViewModel(DiagnosticReport? report, RuleConfigurationDocument? configuration,
        AnalysisProgress? progress = null, IDiagnosticPackageWriter? writer = null, IUnexpectedErrorReporter? unexpectedErrors = null,
        string? sourceNotice = null)
    {
        _report = report; _configuration = configuration;
        _progress = progress == null ? null : progress with { Stages = Array.AsReadOnly(progress.Stages.ToArray()), Detail = "" };
        _includeReport = report != null;
        _builder = new(unexpectedErrors); _exporter = new(writer, unexpectedErrors); _unexpectedErrors = unexpectedErrors;
        SourceNotice = sourceNotice ?? "";
    }
    public string SourceNotice { get; }
    public bool HasReport => _report != null;
    public string Scope => _report == null ? "无可用报告：可收集环境、配置和状态。" : $"报告快照：{_report.Kind} / {_report.Scope} / {_report.CreatedAt:O}";
    public bool IncludeReport { get => _includeReport; set { if (!_busy && _includeReport != value) { _includeReport = value; Invalidate(); } } }
    public bool IncludeRawSelection { get => _includeRawSelection; set { if (!_busy && _includeRawSelection != value) { _includeRawSelection = value; Invalidate(); } } }
    public string? LogPath { get => _logPath; set { if (!_busy && _logPath != value) { _logPath = value; Invalidate(); } } }
    public string? DumpPath { get => _dumpPath; set { if (!_busy && _dumpPath != value) { _dumpPath = value; Invalidate(); } } }
    public bool CanEdit => !_busy;
    public bool CanSave => !_busy && _package != null;
    public bool IsBusy => _busy;
    public DiagnosticPackage? Package => _package;
    public IReadOnlyList<DiagnosticPackageEntry> Entries => _package?.Entries ?? Array.Empty<DiagnosticPackageEntry>();
    public DiagnosticPackageEntry? SelectedEntry { get => _selectedEntry; set { _selectedEntry = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewText)); } }
    public string PreviewText => SelectedEntry?.Preview ?? "生成预览后，选择清单中的文件查看实际文本。DUMP 仅显示大小和校验值，需另用本地调试器检查。";
    public string PrivacyNotice => _package?.PrivacyNotice ?? "原始 XML、日志和 DUMP 默认不附带。日志和 DUMP 无自动脱敏；选择后将按原样保存。";
    public string Status => _status;
    public string InventorySummary => _package == null ? "尚未生成文件清单。" : $"实际文件 {_package.Entries.Count} 项，共 {_package.TotalBytes:N0} 字节（压缩前）；manifest 和 SHA256SUMS 也在清单内。";

    public async Task PrepareAsync(CancellationToken token = default)
    {
        if (_busy) return;
        var options = new DiagnosticPackageOptions(IncludeReport, IncludeRawSelection, LogPath, DumpPath);
        _package = null; _selectedEntry = null; _busy = true; _status = "正在准备本地审核快照…"; Notify();
        try
        {
            var result = await Task.Run(() => _builder.Prepare(_report, _configuration, _progress, options, token));
            token.ThrowIfCancellationRequested();
            _package = result.Package; _status = result.Message;
            _selectedEntry = _package?.Entries.FirstOrDefault(e => e.Name == "manifest.json");
        }
        catch (Exception exception) { _status = ExceptionPolicy.Describe(exception, "IMP27.Package.Review", _unexpectedErrors); }
        finally { _busy = false; Notify(); }
    }

    public async Task SaveAsync(string path, CancellationToken token = default)
    {
        if (!CanSave || _package == null) return;
        var package = _package;
        _busy = true; _status = "正在保存并校验已审核内容…"; Notify();
        try { _status = (await Task.Run(() => _exporter.Save(package, path, token))).Message; }
        catch (Exception exception) { _status = ExceptionPolicy.Describe(exception, "IMP27.Package.ReviewSave", _unexpectedErrors); }
        finally { _busy = false; Notify(); }
    }
    private void Invalidate()
    {
        _package = null; _selectedEntry = null; _status = "内容选择已改变，请重新生成预览后保存。"; Notify();
    }
    private void Notify()
    {
        foreach (string name in new[] { nameof(IncludeReport), nameof(IncludeRawSelection), nameof(LogPath), nameof(DumpPath),
            nameof(CanEdit), nameof(CanSave), nameof(IsBusy), nameof(Package), nameof(Entries), nameof(SelectedEntry),
            nameof(PreviewText), nameof(PrivacyNotice), nameof(Status), nameof(InventorySummary) }) OnPropertyChanged(name);
    }
}
