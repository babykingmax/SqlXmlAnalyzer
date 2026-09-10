using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.ViewModels;

public sealed record DeadlockSourceTarget(DeadlockProcess? Process, LockResource? Resource, string? EdgeId,
    SourceLocation? Location, string Sql, string Xml)
{
    public string Description => WorkspaceSourceFormatter.Location(Location);
    public IReadOnlyList<string> LinkIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ProcessIds { get; init; } = Array.Empty<string>();
}

public sealed class DeadlockWorkspaceViewModel : ObservableObject
{
    private readonly IUnexpectedErrorReporter _unexpectedErrors;
    private DeadlockPattern? _pattern;
    private DeadlockEvidence? _selectedEvidence;
    private readonly Dictionary<string, XElement> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sourcePaths = new(StringComparer.Ordinal);
    private XDocument? _originalDocument;
    private bool _sourceChanged;
    internal bool IsSynchronizingView { get; set; }
    public DeadlockWorkspaceViewModel(IUnexpectedErrorReporter? unexpectedErrors = null) =>
        _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
    public DeadlockInput? SelectedEvent { get; private set; }
    public DeadlockAnalysisOutput? Analysis { get; private set; }
    public Reporting.DiagnosticReport CreateReport(DocumentEnvelope? envelope = null)
    {
        if (_sourceChanged || SelectedEvent == null || Analysis == null)
            throw new InvalidDataException("事件源已改变或分析尚未完成，请重新分析后导出。");
        return Reporting.DiagnosticReportFactory.Deadlock(SelectedEvent, Analysis, envelope);
    }
    public string Context { get; private set; } = "尚未打开死锁事件。";
    public string Status { get; private set; } = "尚无等待关系证据。";
    public string Nature => "依赖推演：死锁快照不包含原始时间序列；步骤只解释依赖关系，不是真实回放。";
    public IReadOnlyList<DeadlockEvidence> Evidence => _pattern?.Evidence ?? Array.Empty<DeadlockEvidence>();
    public DeadlockPattern? SelectedPattern => _pattern;
    public DeadlockSourceTarget? SourceTarget { get; private set; }
    public DeadlockEvidence? SelectedEvidence
    {
        get => _selectedEvidence;
        set
        {
            if (ReferenceEquals(value, _selectedEvidence) && !_sourceChanged) return;
            Execute(() =>
            {
                if (value != null && !Evidence.Any(e => ReferenceEquals(e, value))) throw new InvalidDataException("证据不属于当前事件的诊断。");
                var target = value == null ? null : Resolve(value);
                _selectedEvidence = value;
                SourceTarget = target;
                OnPropertyChanged(nameof(SelectedEvidence)); OnPropertyChanged(nameof(SourceTarget));
            });
        }
    }

    public void Begin(InputRecognitionResult? input, DeadlockInput selected) => Execute(() =>
    {
        if (input != null && !input.Deadlocks.Any(e => ReferenceEquals(e, selected)))
            throw new InvalidDataException("所选事件不属于当前输入。");
        Clear(); SelectedEvent = selected;
        selected.Document.Changed += SourceChanged;
        _originalDocument = selected.OriginalElement?.Document;
        if (_originalDocument != null && !ReferenceEquals(_originalDocument, selected.Document)) _originalDocument.Changed += SourceChanged;
        Context = WorkspaceSourceFormatter.Context(input?.Envelope, input?.Capabilities ?? DocumentCapabilities.DeadlockGraph, input?.Status)
            + Environment.NewLine + selected.DisplayName + "；采集时间：" + (selected.CapturedAt?.ToString("O") ?? "未采集带时区时间")
            + Environment.NewLine + WorkspaceSourceFormatter.Location(selected.Location);
        if (input?.Diagnostics.Count > 0) Context += Environment.NewLine + InputReadPresentation.Describe(input);
        var root = selected.OriginalElement ?? selected.Document.Root;
        if (root != null) IndexSources(root, "/" + root.Name.LocalName + "[1]", selected.Location?.XmlPath ?? "/" + root.Name + "[1]");
        Status = "正在分析当前事件；旧事件的诊断与证据已清空。";
        OnPropertyChanged(nameof(SelectedEvent)); OnPropertyChanged(nameof(Context)); OnPropertyChanged(nameof(Status));
        Logger.Debug("IMP20 deadlock event selected; prior evidence cleared.");
    });

    private void IndexSources(XElement element, string path, string originalPath)
    {
        _sources[path] = element;
        _sourcePaths[path] = originalPath;
        var counts = new Dictionary<XName, int>();
        foreach (var child in element.Elements())
        {
            counts.TryGetValue(child.Name, out int count); counts[child.Name] = ++count;
            IndexSources(child, path + "/" + child.Name.LocalName + $"[{count}]", originalPath + "/" + child.Name + $"[{count}]");
        }
    }

    public bool Complete(XDocument document, DeadlockAnalysisOutput analysis)
    {
        if (SelectedEvent == null || !ReferenceEquals(document, SelectedEvent.Document))
        {
            Logger.Warning("IMP20 obsolete deadlock result ignored."); return false;
        }
        if (_sourceChanged) { ReportFailure(new InvalidDataException("事件原始 XML 已改变，请重新分析。")); return false; }
        Analysis = analysis;
        Status = $"当前事件：进程 {analysis.Processes.Count} / 资源 {analysis.Resources.Count} / 受害者 {analysis.Graph.VictimProcessIds.Count}。"
            + analysis.Graph.CycleAnalysis.Summary;
        if (analysis.Patterns.Count == 0) Status += " 无匹配诊断；不代表没有死锁。";
        if (analysis.Warnings.Count > 0) Status += Environment.NewLine + string.Join(Environment.NewLine, analysis.Warnings);
        OnPropertyChanged(nameof(Analysis)); OnPropertyChanged(nameof(Status));
        Logger.Debug($"IMP20 deadlock event applied: processes={analysis.Processes.Count}, resources={analysis.Resources.Count}.");
        return true;
    }

    public void SelectPattern(DeadlockPattern pattern) => Execute(() =>
    {
        if (Analysis == null || !Analysis.Patterns.Any(p => ReferenceEquals(p, pattern)))
            throw new InvalidDataException("诊断不属于当前事件，请重新选择。");
        _pattern = pattern; _selectedEvidence = null; SourceTarget = null;
        OnPropertyChanged(nameof(SelectedPattern));
        OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(SelectedEvidence)); OnPropertyChanged(nameof(SourceTarget));
        if (Evidence.Count > 0) SelectedEvidence = Evidence[0];
        else { Status = "当前诊断没有可定位证据；请核对事件完整性。"; OnPropertyChanged(nameof(Status)); Logger.Warning("IMP20 deadlock diagnostic has no locatable evidence."); }
    });

    public void SelectProcess(DeadlockProcess process) => Execute(() =>
    {
        if (Analysis?.Processes.Any(p => ReferenceEquals(p, process)) != true) throw new InvalidDataException("进程不属于当前事件。");
        SelectManualTarget(new("process", "", ProcessId: process.Id, Source: process.Source));
    });
    public void SelectResource(LockResource resource) => Execute(() =>
    {
        if (Analysis?.Resources.Any(r => ReferenceEquals(r, resource)) != true) throw new InvalidDataException("资源不属于当前事件。");
        SelectManualTarget(new("resource", "", ResourceId: resource.Id, Source: resource.Source));
    });

    private void SelectManualTarget(DeadlockEvidence evidence)
    {
        var target = Resolve(evidence);
        _selectedEvidence = null;
        SourceTarget = target;
        // Keep the diagnostic's evidence available, but never label a manual target as that evidence.
        OnPropertyChanged(nameof(SelectedEvidence));
        OnPropertyChanged(nameof(SourceTarget));
        Logger.Debug("IMP20 manual deadlock target selected; evidence selection reset.");
    }

    private DeadlockSourceTarget Resolve(DeadlockEvidence evidence)
    {
        if (_sourceChanged) throw new InvalidDataException("事件原始 XML 已改变，请重新分析。");
        var process = Analysis!.Processes.SingleOrDefault(p => p.Id == evidence.ProcessId);
        var resource = Analysis.Resources.SingleOrDefault(r => r.Id == evidence.ResourceId);
        var source = evidence.Source ?? resource?.Source ?? process?.Source;
        XElement? element = source == null ? null : _sources.GetValueOrDefault(source.XmlPath);
        var location = source;
        if (source != null && SelectedEvent?.Location is { } eventLocation)
        {
            string prefix = "/" + SelectedEvent.Document.Root!.Name + "[1]";
            string path = _sourcePaths.GetValueOrDefault(source.XmlPath)
                ?? (source.XmlPath.StartsWith(prefix, StringComparison.Ordinal)
                    ? eventLocation.XmlPath + source.XmlPath[prefix.Length..] : source.XmlPath);
            var line = element as IXmlLineInfo;
            location = source with { XmlPath = path, EventIndex = eventLocation.EventIndex ?? SelectedEvent.Index,
                Line = line?.HasLineInfo() == true ? line.LineNumber : null,
                Column = line?.HasLineInfo() == true ? line.LinePosition : null,
                ByteOffset = eventLocation.ByteOffset, ByteLength = eventLocation.ByteLength };
        }
        Logger.Debug("IMP20 deadlock source evidence resolved.");
        var dependency = string.IsNullOrEmpty(evidence.EdgeId) ? null : Analysis.Graph.Edges.SingleOrDefault(edge => edge.EdgeId == evidence.EdgeId);
        var linkIds = dependency == null
            ? Analysis.Graph.ResourceLinks.Where(link => link.LinkId == evidence.EdgeId).Select(link => link.LinkId).ToArray()
            : new[] { dependency.WaiterLinkId, dependency.OwnerLinkId }.Where(id => !string.IsNullOrEmpty(id)).ToArray();
        var processIds = dependency == null ? process == null ? Array.Empty<string>() : new[] { process.Id }
            : new[] { dependency.FromProcessId, dependency.ToProcessId };
        return new(process, resource, evidence.EdgeId, location,
            process?.Inputbuf ?? "未采集对应进程 SQL。", element?.ToString() ?? resource?.RawXml ?? process?.RawXml ?? "未采集对应 XML。")
            { LinkIds = linkIds, ProcessIds = processIds };
    }

    public void ReportFailure(Exception exception) => Execute(() => throw exception);
    private void Execute(Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            Analysis = null; _pattern = null; _selectedEvidence = null; SourceTarget = null;
            Status = "事件选择或证据定位失败：" + ExceptionPolicy.Describe(exception, "IMP20.DeadlockWorkspace.Selection", _unexpectedErrors);
            OnPropertyChanged(nameof(Analysis)); OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(SelectedEvidence));
            OnPropertyChanged(nameof(SelectedPattern));
            OnPropertyChanged(nameof(SourceTarget)); OnPropertyChanged(nameof(Status));
        }
    }
    public void Clear()
    {
        if (SelectedEvent != null) SelectedEvent.Document.Changed -= SourceChanged;
        if (_originalDocument != null) _originalDocument.Changed -= SourceChanged;
        _originalDocument = null; _sourceChanged = false;
        SelectedEvent = null; Analysis = null; _pattern = null; _selectedEvidence = null; SourceTarget = null; _sources.Clear(); _sourcePaths.Clear();
        Context = "尚未打开死锁事件。"; Status = "尚无等待关系证据。";
        OnPropertyChanged(nameof(SelectedEvent)); OnPropertyChanged(nameof(Analysis)); OnPropertyChanged(nameof(Context));
        OnPropertyChanged(nameof(SelectedPattern));
        OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(SelectedEvidence)); OnPropertyChanged(nameof(SourceTarget));
    }
    private void SourceChanged(object? sender, XObjectChangeEventArgs e) => _sourceChanged = true;
}
