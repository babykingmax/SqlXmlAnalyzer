using System.ComponentModel;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.ViewModels;

public sealed class RuleConfigurationRow : ObservableObject
{
    public const string DefaultLabel = "默认（按规则）";
    public static IReadOnlyList<string> Choices { get; } = new[] { DefaultLabel, "Info", "Warning", "Critical" };
    private bool _enabled;
    private string _severity;
    public RuleConfigurationRow(RuleMetadata metadata, RuleConfigurationSnapshot setting)
    { Metadata = metadata; _enabled = setting.Enabled; _severity = setting.SeverityOverride ?? DefaultLabel; }
    public RuleMetadata Metadata { get; }
    public string RuleId => Metadata.RuleId;
    public string Category => Metadata.Category.ToString();
    public string Scope => Metadata.Scope switch { RuleScope.Plan => "计划", RuleScope.QueryPlan => "查询计划", RuleScope.Statement => "语句", _ => "算子" };
    public string Description => Metadata.Description;
    public string DefaultSeverity => Metadata.DefaultSeverity;
    public IReadOnlyList<string> SeverityChoices => Choices;
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string SeveritySelection { get => _severity; set => SetProperty(ref _severity, value); }
    public RuleConfigurationSnapshot Capture() => new(RuleId, Enabled, SeveritySelection == DefaultLabel ? null : SeveritySelection);
}

public sealed class RuleConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly RuleConfigurationSession _session;
    private readonly IRuleConfigurationStore _store;
    private readonly IUnexpectedErrorReporter _reporter;
    private readonly Action<bool> _applied;
    private readonly Func<CancellationToken, Task> _recalculate;
    private readonly Func<bool> _canRecalculate;
    private RuleConfigurationFile? _file;
    private RuleConfigurationDocument _baseline = RuleConfigurationDocument.Defaults;
    private RuleConfigurationDocument? _draft;
    private CancellationTokenSource? _pending;
    private bool _ready, _updating, _disposed;
    private string _filter = "";
    private bool _onlyChanged;

    public RuleConfigurationViewModel(RuleConfigurationSession session, Action<bool>? applied = null,
        Func<CancellationToken, Task>? recalculate = null, Func<bool>? canRecalculate = null,
        IRuleConfigurationStore? store = null, IUnexpectedErrorReporter? reporter = null)
    {
        _session = session; _store = store ?? new RuleConfigurationStore(reporter: reporter);
        _reporter = reporter ?? UnexpectedErrorReporter.Shared; _applied = applied ?? (_ => { });
        _recalculate = recalculate ?? (_ => Task.CompletedTask); _canRecalculate = canRecalculate ?? (() => false);
        ReplaceRows(_baseline);
    }
    public IReadOnlyList<RuleConfigurationRow> Rows { get; private set; } = [];
    public IReadOnlyList<RuleConfigurationRow> VisibleRows { get; private set; } = [];
    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) RefreshVisibleRows(); } }
    public bool OnlyChanged { get => _onlyChanged; set { if (SetProperty(ref _onlyChanged, value)) RefreshVisibleRows(); } }
    public bool IsBusy { get; private set; }
    public bool CanEdit => _ready && !IsBusy && !_disposed;
    public bool CanApply => CanEdit && _draft != null;
    public bool CanSave => CanApply && _file != null;
    public bool CanRecalculate => CanApply && _draft!.Fingerprint == _session.Current?.Fingerprint && _canRecalculate();
    public string FilePath => _file?.Path ?? RuleConfigurationPathResolver.Resolve();
    public string ActiveConfiguration => "活动配置：" + (_session.Current?.Fingerprint[..12] ?? "无有效配置")
        + (_draft == null ? "；草稿无效。" : _draft.Fingerprint == _session.Current?.Fingerprint ? "；草稿与活动配置一致。" : "；草稿尚未应用。")
        + " 配置切换后须重算已有结果；保存与应用是独立操作。";
    public string ScopeDescription => "本界面配置执行计划诊断规则，死锁分析与 SQL 改写安全策略不由这些开关控制。"
        + "默认基准是规则元数据，选择“默认”仍由规则按证据确定严重度。未知规则及未知 JSON 字段保留，不在本版本执行。"
        + "同证据诊断合并时保留所有命中来源并取最高严重度（如 RULE_004 与 RULE_030），单个开关不屏蔽其他规则。"
        + "应用仅影响本次会话；重启读取程序目录中的 RuleConfiguration.json。";
    public string Status { get; private set; } = "正在准备规则配置。";
    public string Preview { get; private set; } = "";
    public string Notices => string.Join(Environment.NewLine, _baseline.Warnings);
    public int ChangeCount { get; private set; }
    public RuleConfigurationDocument? Draft => _draft;

    public Task InitializeAsync() => LoadAsync(null);
    public Task LoadAsync(string? path) => RunAsync(async token =>
    {
        var file = await _store.LoadAsync(path, token);
        token.ThrowIfCancellationRequested();
        if (_disposed) return;
        _file = file; ReplaceRows(file.Document);
        Status = file.Snapshot == null ? "默认文件不存在；当前显示内置默认值，尚未写入。" : "配置已读取；预览修改后可保存或应用。";
        OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(Notices));
    });
    public Task SaveAsync() => !CanSave ? Task.CompletedTask : SaveCoreAsync(null);
    public Task SaveAsAsync(string path) => !CanApply ? Task.CompletedTask : SaveCoreAsync(path);
    private Task SaveCoreAsync(string? path)
    {
        var draft = _draft!; var file = _file;
        return RunAsync(async token =>
        {
            var outcome = path == null ? await _store.SaveAsync(file!, draft, token) : await _store.SaveAsAsync(path, draft, token);
            if (_disposed) return;
            Status = outcome.Message;
            if (outcome.Succeeded)
            {
                _file = outcome.File!; ReplaceRows(draft);
                Status += " 应用状态见活动配置说明。";
                OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(Notices));
            }
        });
    }

    public void RestoreDefaults()
    {
        if (!CanEdit) return;
        _updating = true;
        try { foreach (var row in Rows) { row.Enabled = true; row.SeveritySelection = RuleConfigurationRow.DefaultLabel; } }
        finally { _updating = false; }
        RebuildDraft(); Status = "已恢复已知规则的内置默认值草稿；未知项保留。请核对预览后保存或应用。";
        OnPropertyChanged(nameof(Status));
    }
    public void Apply()
    {
        if (!CanApply) return;
        bool committed = false;
        try
        {
            bool changed = _session.Apply(_draft!); committed = true;
            _applied(changed);
            Status = changed ? "规则配置已应用到本次会话；已有分析结果需要重新计算。" : "配置与活动快照一致，无需因配置重新计算。";
            Logger.Debug($"IMP24 configuration applied: changed={changed}.");
        }
        catch (Exception exception)
        { Status = (committed ? "配置已应用，但界面更新失败：" : "配置应用失败：") + ExceptionPolicy.Describe(exception, "IMP24.Configuration.Apply", _reporter); }
        NotifyState();
    }
    public Task RecalculateAsync() => !CanRecalculate ? Task.CompletedTask : RunAsync(async token =>
    {
        await _recalculate(token);
        token.ThrowIfCancellationRequested();
        if (!_disposed) Status = "已完成当前计划的重新分析请求；结果与配置状态请查看工作区。";
    });

    private void ReplaceRows(RuleConfigurationDocument document)
    {
        foreach (var row in Rows) row.PropertyChanged -= RowChanged;
        _baseline = document;
        var engine = new RuleEngine(configuration: RuleConfigurationDocument.Defaults); engine.RegisterDefaultRules();
        Rows = engine.RegisteredRules.Select(rule => new RuleConfigurationRow(rule.Metadata, document.Get(rule.RuleId)))
            .OrderBy(row => row.RuleId, StringComparer.Ordinal).ToArray();
        foreach (var row in Rows) row.PropertyChanged += RowChanged;
        OnPropertyChanged(nameof(Rows)); RebuildDraft();
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs args) { if (!_updating && !_disposed) RebuildDraft(); }
    private bool Changed(RuleConfigurationRow row) => row.Capture() != _baseline.Get(row.RuleId);
    private void RebuildDraft()
    {
        try
        {
            _draft = _baseline.WithSettings(Rows.Select(row => row.Capture()));
            var changes = Rows.Where(Changed).ToArray(); ChangeCount = changes.Length;
            Preview = changes.Length == 0 ? "与已载入配置相比，没有已知规则变更。" : string.Join(Environment.NewLine, changes.Select(row =>
            {
                var before = _baseline.Get(row.RuleId); var after = row.Capture();
                return $"{row.RuleId}：启用 {before.Enabled} → {after.Enabled}；严重度 {before.SeverityOverride ?? "默认"} → {after.SeverityOverride ?? "默认"}";
            }));
        }
        catch (Exception exception)
        {
            _draft = null; Preview = "草稿无效，不能保存或应用。";
            Status = ExceptionPolicy.Describe(exception, "IMP24.Configuration.Draft", _reporter);
        }
        OnPropertyChanged(nameof(ChangeCount)); OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(Draft));
        RefreshVisibleRows(); NotifyState();
    }
    private void RefreshVisibleRows()
    {
        string query = Filter.Trim();
        var visible = Rows.Where(row => (!OnlyChanged || Changed(row)) &&
            (row.RuleId + " " + row.Category + " " + row.Scope + " " + row.Description).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!VisibleRows.SequenceEqual(visible)) { VisibleRows = visible; OnPropertyChanged(nameof(VisibleRows)); }
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_disposed || IsBusy) return;
        using var cancellation = new CancellationTokenSource(); _pending = cancellation;
        IsBusy = true; Status = "正在处理规则配置…"; NotifyState();
        try { await action(cancellation.Token); }
        catch (Exception exception)
        {
            string detail = ExceptionPolicy.Describe(exception, "IMP24.Configuration.Operation", _reporter);
            if (!_disposed) Status = "操作未完成；草稿和上次活动配置保留。" + detail;
        }
        finally { _pending = null; _ready = true; IsBusy = false; if (!_disposed) NotifyState(); }
    }
    private void NotifyState()
    {
        foreach (string property in new[] { nameof(IsBusy), nameof(CanEdit), nameof(CanApply), nameof(CanSave), nameof(CanRecalculate), nameof(ActiveConfiguration), nameof(Status) })
            OnPropertyChanged(property);
    }
    public void RefreshAnalysisState() => NotifyState();
    public void Cancel() => _pending?.Cancel();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _pending?.Cancel();
        foreach (var row in Rows) row.PropertyChanged -= RowChanged;
    }
}
