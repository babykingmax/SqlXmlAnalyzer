using System.Collections.ObjectModel;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Refactoring;
using SqlXmlAnalyzer.Application.Models;
using System.Text.Json;

namespace SqlXmlAnalyzer.ViewModels;

public sealed class RewriteReviewViewModel : ObservableObject
{
    private readonly string _source;
    private readonly RewriteProposalService _service;
    private readonly IUnexpectedErrorReporter _unexpectedErrors;
    private RewriteReview _review;
    private string _error = "";
    private readonly ReviewedSqlApplyService _applyService;
    private PreparedSqlRewrite? _prepared;
    private string _validationDetails = "尚未执行数据库语义验证。";
    private bool _busy, _reviewedScenarios, _applied;
    private ApplySqlRewriteResult? _lastApply;
    private string _applySummary = "";
    private int _selectionVersion;
    public ObservableCollection<RewriteProposalItem> Items { get; }
    public string OriginalSql => _source;
    public string PreviewSql => _review.PreviewSql;
    public string Details => RewriteReviewFormatter.Format(_review, includeSql: true, applicationStatus: ApplyStatusText);
    public string ApplyStatusText => _lastApply?.CommitOutcomeUnknown == true ? "提交结果未知，请核对源文件和备份"
        : _lastApply?.SourceWritten == true ? "已写回" : _lastApply != null ? "未写回" : "尚未应用";
    public string PreviewTitle => "审核 SQL · " + ApplyStatusText;
    public string ApplySummary => _applySummary;
    public string Error => _error;
    public bool CanApply => !_busy && !_applied && _reviewedScenarios && _prepared?.CanApply == true;
    public bool CanValidate => !_busy && !_applied && _review.Proposals.Any(p => p.IsSelected);
    public bool CanAcknowledge => !_busy && !_applied && _prepared?.CanApply == true;
    public bool CanSelect => !_busy && !_applied;
    public string ValidationDetails => _validationDetails;
    public bool ReviewedScenarios
    {
        get => _reviewedScenarios;
        set { _reviewedScenarios = value && CanAcknowledge; OnPropertyChanged(); OnPropertyChanged(nameof(CanApply)); }
    }

    public RewriteReviewViewModel(string source, RewriteReview review, IUnexpectedErrorReporter? unexpectedErrors = null,
        ReviewedSqlApplyService? applyService = null)
    {
        _source = source;
        _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
        _service = new(_unexpectedErrors);
        _applyService = applyService ?? new(new SqlWritebackService(new PhysicalSqlWritebackFileSystem(), unexpectedErrors: _unexpectedErrors),
            new LocalDbSqlSemanticRunner(_unexpectedErrors), _unexpectedErrors);
        _review = _service.Review(source, review.Proposals, review.Proposals.Where(p => p.IsSelected).Select(p => p.Id), review.Warnings);
        Items = new(_review.Proposals.Select(p => new RewriteProposalItem(p, Select)));
    }

    private void Select(string id, bool selected)
    {
        if (_applied) return;
        _lastApply = null; _applySummary = "";
        _selectionVersion++;
        _prepared = null;
        _reviewedScenarios = false;
        _validationDetails = "选择已变化，需要重新执行数据库验证。";
        try
        {
            var ids = _review.Proposals.Where(p => p.IsSelected).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var item = _review.Proposals.Single(p => p.Id == id);
            if (selected)
            {
                ids.Add(id);
                ids.UnionWith(item.DependsOn);
            }
            else
            {
                ids.Remove(id);
                foreach (var dependent in _review.Proposals.Where(p => p.DependsOn.Contains(id))) ids.Remove(dependent.Id);
            }
            _review = _service.Review(_source, _review.Proposals, ids, _review.Warnings);
            _error = "";
        }
        catch (Exception exception)
        {
            _error = ExceptionPolicy.Describe(exception, "RewriteReview.Selection", _unexpectedErrors);
        }
        foreach (var row in Items) row.Update(_review.Proposals.Single(p => p.Id == row.Id).IsSelected);
        OnPropertyChanged(nameof(PreviewSql));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(Error));
        NotifyApplyState();
    }

    public async Task ValidateAsync(string sourcePath, SqlSemanticSuite suite, CancellationToken token = default)
    {
        if (_busy || _applied) return;
        _busy = true; _prepared = null; _reviewedScenarios = false;
        _lastApply = null; _applySummary = "目标文件：" + sourcePath;
        _validationDetails = "正在隔离数据库中验证原文与所选 SQL……";
        int version = _selectionVersion;
        NotifyApplyState();
        try
        {
            var result = await _applyService.PrepareAsync(sourcePath, _source, _review, suite, token);
            _error = result.Error ?? "";
            _validationDetails = result.Validation == null ? _error :
                JsonSerializer.Serialize(result.Validation, new JsonSerializerOptions
                { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            if (version == _selectionVersion && !token.IsCancellationRequested) _prepared = result.Prepared;
            else _error = "选择已变化或操作已取消，验证结果不能用于应用。";
        }
        catch (Exception exception) { _error = ExceptionPolicy.Describe(exception, "RewriteReview.Validate", _unexpectedErrors); }
        finally { _busy = false; NotifyApplyState(); OnPropertyChanged(nameof(Error)); }
    }

    public ApplySqlRewriteResult? Apply(CancellationToken token = default)
    {
        if (!CanApply || _prepared == null) return null;
        _busy = true; NotifyApplyState();
        try
        {
            var result = _applyService.Apply(_prepared, _review, _reviewedScenarios, token);
            _lastApply = result;
            _applied = result.SourceWritten || result.CommitOutcomeUnknown;
            _error = result.Error ?? "";
            _applySummary += result.Writeback is { } writeback
                ? $"\n备份：{writeback.BackupPath ?? "无"}；源 hash：{writeback.SourceSha256}；输出 hash：{writeback.OutputSha256}"
                : "\n" + result.Error;
            _validationDetails += $"\n应用结果：{ApplyStatusText}\n{_applySummary}\n{result.Error}";
            return result;
        }
        finally { _busy = false; _prepared = null; NotifyApplyState(); OnPropertyChanged(nameof(Error)); }
    }

    private void NotifyApplyState()
    {
        OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(CanValidate)); OnPropertyChanged(nameof(CanSelect)); OnPropertyChanged(nameof(CanAcknowledge));
        OnPropertyChanged(nameof(ValidationDetails)); OnPropertyChanged(nameof(ReviewedScenarios));
        OnPropertyChanged(nameof(ApplyStatusText)); OnPropertyChanged(nameof(ApplySummary));
        OnPropertyChanged(nameof(PreviewTitle)); OnPropertyChanged(nameof(Details));
    }
}

public sealed class RewriteProposalItem : ObservableObject
{
    private bool _selected;
    private readonly Action<string, bool> _select;
    public string Id { get; }
    public string Label { get; }
    public bool IsSelected { get => _selected; set { if (value != _selected) _select(Id, value); } }
    internal RewriteProposalItem(RewriteProposal proposal, Action<string, bool> select)
    {
        Id = proposal.Id;
        Label = $"{proposal.RuleId} / {proposal.RuleVersion} — {proposal.Description}";
        _selected = proposal.IsSelected;
        _select = select;
    }
    internal void Update(bool value) => SetProperty(ref _selected, value, nameof(IsSelected));
}
