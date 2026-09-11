using System.Globalization;
using System.IO;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.ViewModels;

public sealed class XelSearchViewModel : ObservableObject, IDisposable
{
    private readonly IXelSearchService _service;
    private readonly IUnexpectedErrorReporter _reporter;
    private readonly Func<XelSearchEvent, Task> _navigate;
    private XelSearchCatalog _catalog = new([], [], 0, "");
    private CancellationTokenSource? _pending;
    private long _version;
    private bool _disposed;
    private bool _initializing;
    private bool _hasCatalog;
    private XelSearchGroup? _selectedGroup;
    private XelSearchRow? _selectedEvent;

    public XelSearchViewModel(Func<XelSearchEvent, Task> navigate, IXelSearchService? service = null, IUnexpectedErrorReporter? reporter = null)
    {
        _navigate = navigate; _service = service ?? new XelSearchService(); _reporter = reporter ?? UnexpectedErrorReporter.Shared;
    }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Database { get; set; } = "";
    public string Object { get; set; } = "";
    public string Text { get; set; } = "";
    public string Offset { get; set; } = "+00:00";
    public bool IsBusy { get; private set; }
    public bool CanSearch => !_initializing && _hasCatalog && !_disposed;
    public bool CanImport => !_initializing && !_disposed;
    public bool CanNavigate => !IsBusy && SelectedEvent != null && !_disposed;
    public string Status { get; private set; } = "添加 XEL 文件，或检索当前已打开的死锁输入。";
    public string Scope => "筛选范围：已载入的有效死锁及其 XML、采集字段/动作；数据库/对象/文本为忽略大小写的字面包含匹配，条件之间为 AND。时间两端均包含。"
        + "跨文件按 UTC → 路径 → 源 SHA-256 → 事件/子事件排序。相同 UTC 时刻、XML 和采集字段/动作完全相同的副本标记为重复，全部保留。"
        + "同一路径再次添加会跳过；当前输入沿用主窗口快照，更新时须先在主窗口重新打开文件。上限：32 文件、10,000 记录、128 MiB 输入、32 Mi 字符；取消或导入失败保留上次结果。";
    public string Notices => _catalog.Notices;
    public IReadOnlyList<XelSearchRow> Events { get; private set; } = [];
    public IReadOnlyList<XelSearchGroup> Groups { get; private set; } = [];
    public IReadOnlyList<XelSearchRow> VisibleEvents => SelectedGroup?.Events ?? Events;
    public XelSearchGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (value != null && !Groups.Contains(value)) return;
            if (!SetProperty(ref _selectedGroup, value)) return;
            SelectedEvent = null; OnPropertyChanged(nameof(VisibleEvents));
        }
    }
    public XelSearchRow? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (value != null && !VisibleEvents.Contains(value)) return;
            if (!SetProperty(ref _selectedEvent, value)) return;
            OnPropertyChanged(nameof(Details)); OnPropertyChanged(nameof(CanNavigate));
        }
    }
    public string Details
    {
        get
        {
            if (SelectedEvent is not { } row) return "选择聚合组查看成员，选择事件查看原始位置；“回到原始事件”打开该副本的死锁图与证据。";
            var entry = row.Entry;
            return $"源：{entry.SourcePath}\nSHA-256：{entry.SourceHash}\n{entry.EventLabel}\n原始时间：{entry.OriginalTime}\n显示时间：{row.DisplayTime}\n{entry.Location}\n聚合键：{entry.StructureKey}\n{entry.StructureDescription}\n\n"
                + entry.CapturePreview + "\n\n" + entry.XmlPreview;
        }
    }

    public async Task InitializeAsync(XelSearchSource? source)
    {
        if (_disposed || _initializing) return;
        _initializing = true;
        OnPropertyChanged(nameof(CanSearch)); OnPropertyChanged(nameof(CanImport));
        try
        {
            await RunAsync(async token =>
            {
                var catalog = await _service.LoadAsync(source == null ? [] : [source], [], token).ConfigureAwait(false);
                return (catalog, _service.Search(catalog, new(), token));
            });
        }
        finally
        {
            _initializing = false;
            OnPropertyChanged(nameof(CanSearch)); OnPropertyChanged(nameof(CanImport));
        }
    }

    public Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (!CanImport) return Task.CompletedTask;
        var current = _catalog;
        var readFilter = CaptureFilter();
        return RunAsync(async token =>
        {
            var filter = readFilter();
            var catalog = await _service.LoadAsync(current.Sources, paths, token).ConfigureAwait(false);
            return (catalog, _service.Search(catalog, filter, token));
        });
    }

    public Task SearchAsync()
    {
        if (!CanSearch) return Task.CompletedTask;
        var current = _catalog;
        var readFilter = CaptureFilter();
        return RunAsync(token => Task.FromResult((current, _service.Search(current, readFilter(), token))));
    }

    private Func<XelSearchFilter> CaptureFilter()
    {
        string from = From, to = To, database = Database, obj = Object, text = Text, offset = Offset;
        return () => ReadFilter(from, to, database, obj, text, offset);
    }

    private static XelSearchFilter ReadFilter(string from, string to, string database, string obj, string text, string displayOffset)
    {
        string offset = displayOffset.Trim();
        if (offset.Length != 6 || offset[0] is not ('+' or '-') ||
            !TimeSpan.TryParseExact(offset[1..], @"hh\:mm", CultureInfo.InvariantCulture, out var span))
            throw new InvalidDataException("显示偏移须为 ±HH:mm，例如 +08:00。");
        if (offset[0] == '-') span = -span;
        return new(XelSearchFilter.ParseTime(from), XelSearchFilter.ParseTime(to), database, obj, text, span);
    }

    private async Task RunAsync(Func<CancellationToken, Task<(XelSearchCatalog Catalog, XelSearchResult Result)>> action)
    {
        if (_disposed) return;
        _pending?.Cancel();
        var cancellation = new CancellationTokenSource(); _pending = cancellation;
        long version = ++_version;
        IsBusy = true; Status = "正在处理；可取消，上次结果暂时保留。";
        OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanNavigate)); OnPropertyChanged(nameof(Status));
        try
        {
            var outcome = await Task.Run(() => action(cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed || version != _version) return;
            _catalog = outcome.Catalog; Events = outcome.Result.Events; Groups = outcome.Result.Groups;
            _hasCatalog = true;
            OnPropertyChanged(nameof(CanSearch));
            _selectedGroup = null; _selectedEvent = null;
            Status = outcome.Result.Summary;
            foreach (string property in new[] { nameof(Events), nameof(Groups), nameof(SelectedGroup), nameof(SelectedEvent), nameof(VisibleEvents), nameof(Details), nameof(Notices) })
                OnPropertyChanged(property);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.Warning("IMP23 operation cancelled; previous snapshot retained.");
            if (!_disposed && version == _version) Status = "已取消；保留上次已提交的检索结果。";
        }
        catch (Exception exception)
        {
            string detail = ExceptionPolicy.Describe(exception, "IMP23.XelSearch", _reporter);
            if (!_disposed && version == _version) Status = "操作失败；保留上次结果。" + detail;
        }
        finally
        {
            if (version == _version)
            {
                _pending = null; IsBusy = false;
                OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanNavigate)); OnPropertyChanged(nameof(Status));
            }
            cancellation.Dispose();
        }
    }

    public async Task NavigateAsync()
    {
        if (!CanNavigate || SelectedEvent is not { } row) return;
        try
        {
            row.Entry.ValidateSource();
            await _navigate(row.Entry);
            Logger.Debug("IMP23 original event navigation completed.");
        }
        catch (Exception exception)
        {
            Status = "事件追溯失败：" + ExceptionPolicy.Describe(exception, "IMP23.XelSearch.Navigate", _reporter);
            OnPropertyChanged(nameof(Status));
        }
    }
    public void Cancel() { if (!_disposed) _pending?.Cancel(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_version; _pending?.Cancel(); _pending = null;
    }
}
