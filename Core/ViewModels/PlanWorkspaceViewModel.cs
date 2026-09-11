using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.ViewModels;

public sealed record PlanStatementChoice(PlanStatement Statement, PlanQueryPlan? QueryPlan)
{
    public string DisplayName => $"Statement {Statement.Key.StatementOrdinal} · {Statement.Kind}"
        + (Statement.StatementId == null ? "" : $" · ID {Statement.StatementId}")
        + (QueryPlan == null ? " · 未采集算子" : $" · QueryPlan {QueryPlan.Key.QueryPlanOrdinal}");
}

public sealed record PlanWorkspaceIssue(PlanLocation Location, PlanDiagnostic? Diagnostic, RuleRun? Run)
{
    public string Title => Diagnostic?.Title ?? Run!.Reason;
    public string Severity => Diagnostic?.Severity.ToString() ?? (Run?.Status == RuleRunStatus.Failed ? "Critical" : "N/A");
    public string Confidence => Diagnostic?.Confidence.ToString() ?? "N/A";
    public string Status => Diagnostic == null ? Run!.Status.ToString() : "Hit";
    public string Scope => Location.Operator != null ? $"Node {Location.Operator.NodeId ?? "N/A"}"
        : Location.QueryPlan != null ? "查询计划" : Location.Statement != null ? "语句" : "文档";
    public string Details => Diagnostic == null ? DiagnosticTextFormatter.FormatRun(Run!) : DiagnosticTextFormatter.FormatDiagnostic(Diagnostic);
    public IReadOnlyList<DiagnosticEvidence> Evidence => Diagnostic?.Evidence ?? Array.Empty<DiagnosticEvidence>();
    public string RuleId => Diagnostic?.RuleId ?? Run?.RuleId ?? "";
    public string StatusLabel => Diagnostic != null ? "发现问题" : Run?.Status == RuleRunStatus.Failed ? "检查失败" : "未执行";
    public string Summary => Diagnostic?.Summary ?? Run?.Reason ?? "";
}

public sealed record PlanWorkspaceSelection(PlanStatementChoice Choice, IReadOnlyList<XElement> Operators,
    IReadOnlyList<PlanWorkspaceIssue> Issues, IReadOnlyList<MissingIndexSuggestion> MissingIndexes, string Status);
public sealed record PlanSourceTarget(PlanLocation Location, string Sql, string Xml)
{
    public string Description => WorkspaceSourceFormatter.Location(Location.Source) + Environment.NewLine + Location.DisplayScope;
}

/// <summary>One selection owns SQL, operators, issues and source navigation. XML remains in its original document.</summary>
public sealed partial class PlanWorkspaceViewModel : ObservableObject
{
    private readonly IUnexpectedErrorReporter _unexpectedErrors;
    private readonly Func<XDocument, PlanDocument?> _readModel;
    private IReadOnlyList<MissingIndexSuggestion> _indexes = Array.Empty<MissingIndexSuggestion>();
    private PlanBatch? _selectedBatch;
    private PlanStatementChoice? _selectedStatement;
    private PlanWorkspaceIssue? _selectedIssue;
    private DiagnosticEvidence? _selectedEvidence;
    public PlanWorkspaceViewModel(IUnexpectedErrorReporter? unexpectedErrors = null, Func<XDocument, PlanDocument?>? readModel = null)
    {
        _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
        _readModel = readModel ?? (document => PlanIdentityAdapter.GetDocument(document));
    }
    public XDocument? Document { get; private set; }
    public PlanDocument? Model { get; private set; }
    public PlanDiagnosticReport? Report { get; private set; }
    private string? _activeConfiguration;
    public bool NeedsConfigurationReanalysis => Report != null && _activeConfiguration != null &&
        _activeConfiguration != Configuration.RuleConfigurationDocument.ComputeFingerprint(Report.Configuration.Values);
    public string ConfigurationStatus => Report == null ? "规则配置：尚无分析结果。" :
        "结果配置 " + Configuration.RuleConfigurationDocument.ComputeFingerprint(Report.Configuration.Values)[..12] +
        (NeedsConfigurationReanalysis ? "；配置已切换，需要重新分析。当前图和列表保留旧快照。" : "；图和列表使用同一配置快照。");
    public void SetActiveConfiguration(string fingerprint)
    {
        _activeConfiguration = fingerprint;
        OnPropertyChanged(nameof(NeedsConfigurationReanalysis)); OnPropertyChanged(nameof(ConfigurationStatus));
        OnPropertyChanged(nameof(SelectedReportText));
        NotifyPresentationHeader();
    }
    public IReadOnlyList<PlanBatch> Batches => Model?.Batches ?? Array.Empty<PlanBatch>();
    public IReadOnlyList<PlanStatementChoice> Statements { get; private set; } = Array.Empty<PlanStatementChoice>();
    public PlanWorkspaceSelection? Selection { get; private set; }
    public IReadOnlyList<PlanWorkspaceIssue> Issues => Selection?.Issues ?? Array.Empty<PlanWorkspaceIssue>();
    public string Context { get; private set; } = "尚未打开执行计划。";
    public string Status { get; private set; } = "尚未采集计划。";
    public string RefactoringNotices { get; private set; } = "";
    public bool HasRefactoringNotices => !string.IsNullOrWhiteSpace(RefactoringNotices);
    public string SelectedReportText => Selection == null ? "" :
        $"当前选择：{Selection.Choice.Statement.Key} / {Selection.Choice.DisplayName}" + Environment.NewLine + ConfigurationStatus + Environment.NewLine + Status
        + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, Issues.Select(issue => issue.Details))
        + (HasRefactoringNotices ? Environment.NewLine + Environment.NewLine + "SQL 改写阶段（文档分析结果）："
            + Environment.NewLine + RefactoringNotices : "");
    public string Details => SelectedIssue?.Details ?? (SourceTarget?.Location.Operator == null
        ? "请选择问题或规则运行记录；空列表不代表计划健康。"
        : "当前节点没有匹配问题；可查看原始证据，这不代表所有检查都已通过。");
    public Reporting.DiagnosticReport CreateReport()
    {
        if (Document == null || Model == null || Report == null || Selection == null)
            throw new InvalidDataException("当前选择没有完整诊断快照，请等待分析完成或重新分析。");
        return Reporting.DiagnosticReportFactory.Plan(Document, Model, Report, Selection.Choice.Statement.Key,
            Selection.Choice.QueryPlan?.Key, RefactoringNotices, Context + Environment.NewLine + ConfigurationStatus + Environment.NewLine + Status);
    }
    public IReadOnlyList<DiagnosticEvidence> Evidence => SelectedIssue?.Evidence ?? Array.Empty<DiagnosticEvidence>();
    public PlanSourceTarget? SourceTarget { get; private set; }
    public PlanBatch? SelectedBatch
    {
        get => _selectedBatch;
        set { if (!ReferenceEquals(value, _selectedBatch) && value != null) Execute(() => SelectBatch(value)); }
    }
    public PlanStatementChoice? SelectedStatement
    {
        get => _selectedStatement;
        set { if (!ReferenceEquals(value, _selectedStatement) && value != null) Execute(() => SelectStatement(value)); }
    }
    public PlanWorkspaceIssue? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (ReferenceEquals(value, _selectedIssue)) return;
            Execute(() =>
            {
                if (value != null && !Issues.Contains(value)) throw new InvalidDataException("所选诊断不属于当前范围，请重新选择。");
                _selectedIssue = value; _selectedEvidence = null;
                SourceTarget = value == null ? null : Resolve(value.Location);
                NotifyEvidence();
            });
        }
    }
    public DiagnosticEvidence? SelectedEvidence
    {
        get => _selectedEvidence;
        set
        {
            if (value == null || ReferenceEquals(value, _selectedEvidence)) return;
            Execute(() =>
            {
                if (!Evidence.Contains(value)) throw new InvalidDataException("证据不属于所选诊断。");
                var target = Resolve(value.Location);
                var issue = _selectedIssue;
                if (value.Location.Statement != null && (value.Location.Statement != _selectedStatement?.Statement.Key
                    || (value.Location.QueryPlan != null && value.Location.QueryPlan != _selectedStatement?.QueryPlan?.Key)))
                {
                    var batch = Batches.Single(b => b.Key == value.Location.Batch);
                    SelectBatch(batch);
                    SelectStatement(Statements.First(s => s.Statement.Key == value.Location.Statement
                        && (value.Location.QueryPlan == null || s.QueryPlan?.Key == value.Location.QueryPlan)));
                }
                _selectedIssue = issue; _selectedEvidence = value; SourceTarget = target; NotifyEvidence();
            });
        }
    }

    public void Open(XDocument document, PlanDiagnosticReport? report, IReadOnlyList<MissingIndexSuggestion> indexes,
        InputRecognitionResult? input = null, string? refactoringNotices = null)
    {
        Execute(() =>
        {
            Clear();
            var model = _readModel(document) ?? throw new InvalidDataException("计划没有可选择的 Batch / Statement 身份。");
            if (report != null && report.DocumentId != model.Envelope.DocumentId)
                throw new InvalidDataException("诊断与当前文档身份不一致，请重新分析。");
            Document = document; Model = model; Report = report; _indexes = indexes;
            RefactoringNotices = refactoringNotices ?? "";
            OnPropertyChanged(nameof(RefactoringNotices)); OnPropertyChanged(nameof(HasRefactoringNotices));
            Context = WorkspaceSourceFormatter.Context(input?.Envelope ?? model.Envelope,
                input?.Capabilities ?? report?.Capabilities ?? DocumentCapabilities.None, input?.Status);
            OnPropertyChanged(nameof(Batches)); OnPropertyChanged(nameof(Context));
            if (Batches.Count > 0) SelectBatch(Batches[0]);
            else { Status = "未采集 Batch / Statement，无法展示算子。"; OnPropertyChanged(nameof(Status)); }
            Logger.Debug($"IMP20 plan workspace opened: batches={Batches.Count}, statements={model.Statements.Count}.");
        });
    }

    private void SelectBatch(PlanBatch batch)
    {
        if (!Batches.Contains(batch)) throw new InvalidDataException("Batch 不属于当前文档。");
        _selectedBatch = batch;
        Statements = batch.Statements.SelectMany(s => s.QueryPlans.Count == 0
            ? new[] { new PlanStatementChoice(s, null) }
            : s.QueryPlans.Select(q => new PlanStatementChoice(s, q))).ToArray();
        _selectedStatement = null;
        OnPropertyChanged(nameof(SelectedBatch)); OnPropertyChanged(nameof(Statements));
        if (Statements.Count > 0) SelectStatement(Statements[0]);
        else
        {
            Selection = null; Status = "所选 Batch 没有语句；未采集可展示的计划。";
            _selectedIssue = null; _selectedEvidence = null; SourceTarget = null; NotifySelection();
        }
    }

    private void SelectStatement(PlanStatementChoice choice)
    {
        if (!Statements.Contains(choice)) throw new InvalidDataException("Statement / QueryPlan 不属于当前 Batch。");
        // Also validates the source revision even for statements without any RelOp.
        Model!.GetStatementSource(choice.Statement.Key);
        var operators = choice.QueryPlan?.Operators.Select(o => Model.GetOperatorSource(o.Key)!).ToArray() ?? Array.Empty<XElement>();
        bool Includes(PlanLocation location) => location.DocumentId == Model.Envelope.DocumentId
            && Reporting.DiagnosticReportFactory.Includes(location, choice.Statement.Key, choice.QueryPlan?.Key);
        var runs = Report?.Runs.Where(r => Includes(r.Location) && r.ReasonCode != "RULE_OUTSIDE_SCOPE").ToArray() ?? Array.Empty<RuleRun>();
        var issues = (Report?.Diagnostics.Where(d => Includes(d.Location)).Select(d => new PlanWorkspaceIssue(d.Location, d, null))
            ?? Enumerable.Empty<PlanWorkspaceIssue>()).Concat(runs.Where(r => r.Status is RuleRunStatus.Skipped or RuleRunStatus.Failed)
                .Select(r => new PlanWorkspaceIssue(r.Location, null, r))).ToArray();
        string status = Report == null ? "尚无规则执行记录，请重新分析。" :
            $"当前范围（含文档诊断）：Hit {runs.Count(r => r.Status == RuleRunStatus.Hit)} / NoHit {runs.Count(r => r.Status == RuleRunStatus.NoHit)} / Skipped {runs.Count(r => r.Status == RuleRunStatus.Skipped)} / Failed {runs.Count(r => r.Status == RuleRunStatus.Failed)}。";
        if (operators.Length == 0) status += " 未采集当前语句的算子。";
        if (issues.Length == 0) status += " 无匹配问题；不代表计划完全健康。";
        if (runs.Any(r => r.Status == RuleRunStatus.Failed)) status += " 规则执行失败，诊断不完整。";
        if (runs.Any(r => r.ReasonCode == "RULE_MISSING_EVIDENCE")) status += " 部分检查缺少证据而跳过。";
        _selectedStatement = choice;
        Selection = new(choice, operators, issues, _indexes.Where(i => i.Location != null && Includes(i.Location)).ToArray(), status);
        Status = status; _selectedIssue = null; _selectedEvidence = null; SourceTarget = null;
        NotifySelection();
        Logger.Debug($"IMP20 selection applied: operators={operators.Length}, issues={issues.Length}.");
        if (operators.Length == 0 || Report == null) Logger.Warning("IMP20 selection has no operator or diagnostic evidence.");
    }

    private PlanSourceTarget Resolve(PlanLocation location)
    {
        if (Model == null || Document == null || location.DocumentId != Model.Envelope.DocumentId)
            throw new InvalidDataException("证据不属于当前文档，请重新分析。");
        XElement? source = location.Operator != null ? Model.GetOperatorSource(location.Operator)
            : location.QueryPlan != null ? Model.GetQueryPlanSource(location.QueryPlan)
            : location.Statement != null ? Model.GetStatementSource(location.Statement)
            : Document.Root;
        if (source == null) throw new InvalidDataException("原始证据位置已失效，请重新分析。");
        // A document-level target still checks revision; never resolve by local NodeId.
        if (Model.Statements.Count > 0) Model.GetStatementSource(Model.Statements[0].Key);
        string sql = Model.Statements.FirstOrDefault(s => s.Key == location.Statement)?.Text ?? "未定位到语句 SQL。";
        Logger.Debug("IMP20 source evidence resolved.");
        return new(location, sql, source.ToString());
    }

    public void NavigateOperator(XElement element) => Execute(() =>
    {
        var location = Model?.FindLocation(element);
        if (location?.Operator == null || Selection?.Operators.Contains(element) != true)
            throw new InvalidDataException("节点不属于当前选择。");
        _selectedIssue = Findings.FirstOrDefault(issue => issue.Location.Operator == location.Operator);
        _selectedEvidence = null; SourceTarget = Resolve(location); NotifyEvidence();
    });

    public void ReportFailure(Exception exception) => Fail(exception);
    private void Execute(Action action)
    {
        try { action(); }
        catch (Exception exception) { Fail(exception); }
    }
    private void Fail(Exception exception)
    {
        Clear();
        Status = "工作区选择失败，请重新分析：" + ExceptionPolicy.Describe(exception, "IMP20.PlanWorkspace.Selection", _unexpectedErrors);
        OnPropertyChanged(nameof(Status));
    }
    public void Clear()
    {
        Document = null; Model = null; Report = null; Selection = null; _indexes = Array.Empty<MissingIndexSuggestion>();
        _selectedBatch = null; _selectedStatement = null; _selectedIssue = null; _selectedEvidence = null; SourceTarget = null;
        Statements = Array.Empty<PlanStatementChoice>(); Context = "尚未打开执行计划。"; Status = "尚未采集计划。";
        RefactoringNotices = "";
        OnPropertyChanged(nameof(RefactoringNotices)); OnPropertyChanged(nameof(HasRefactoringNotices));
        OnPropertyChanged(nameof(Batches)); OnPropertyChanged(nameof(Statements)); OnPropertyChanged(nameof(SelectedBatch));
        OnPropertyChanged(nameof(Context)); NotifySelection();
    }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(NeedsConfigurationReanalysis)); OnPropertyChanged(nameof(ConfigurationStatus));
        OnPropertyChanged(nameof(SelectedStatement)); OnPropertyChanged(nameof(Issues)); OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(SelectedReportText));
        RefreshPresentationSelection();
        NotifyEvidence(); OnPropertyChanged(nameof(Selection));
    }
    private void NotifyEvidence()
    {
        OnPropertyChanged(nameof(SelectedIssue)); OnPropertyChanged(nameof(SelectedEvidence)); OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(SourceTarget));
        RefreshPresentationDetails();
    }
}
