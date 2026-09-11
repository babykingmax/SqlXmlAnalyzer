using System.Windows.Input;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.ViewModels;

public partial class MainViewModel
{
    private PlanComparisonResult? _comparison;
    private ComparisonScopeOption? _manualScopeA;
    private ComparisonScopeOption? _manualScopeB;
    private string? _comparisonError;
    public PlanComparisonResult? Comparison => _comparison;
    public IReadOnlyList<ComparisonScopeOption> ComparisonScopesA => _comparison?.ScopesA ?? [];
    public IReadOnlyList<ComparisonScopeOption> ComparisonScopesB => _comparison?.ScopesB ?? [];
    public string ComparisonSummary => _comparisonError ?? _comparison?.Summary ?? "尚无比较结果";
    public string ComparisonUnmatchedA => Unmatched(true);
    public string ComparisonUnmatchedB => Unmatched(false);
    private string Unmatched(bool sideA)
    {
        if (_comparison == null || PlanA == null && PlanB == null) return "尚无比较结果";
        var rows = _comparison.Statements.Where(s => sideA
            ? s.A != null && s.B == null || s.QueryA != null && s.QueryB == null
            : s.B != null && s.A == null || s.QueryB != null && s.QueryA == null)
            .Select(s => s.Label + Environment.NewLine + (sideA ? s.A : s.B)?.Statement.Text
                + Environment.NewLine + s.MatchReason);
        return string.Join(Environment.NewLine + Environment.NewLine, rows.DefaultIfEmpty("无未匹配项"));
    }
    public string ComparisonConditions => _comparison == null ? "" : string.Join(Environment.NewLine,
        _comparison.Statements.Select(s => s.Label + "：" + s.Detail + Environment.NewLine
            + "A：" + s.QueryA?.Capture.Summary + "；" + s.QueryA?.Capture.ParameterSummary + Environment.NewLine
            + "B：" + s.QueryB?.Capture.Summary + "；" + s.QueryB?.Capture.ParameterSummary));
    public ComparisonScopeOption? ManualScopeA { get => _manualScopeA; set => SetProperty(ref _manualScopeA, value); }
    public ComparisonScopeOption? ManualScopeB { get => _manualScopeB; set => SetProperty(ref _manualScopeB, value); }
    public int ComparisonSelectionRevision { get; private set; }
    public PlanComparisonSelection ComparisonSelection { get; private set; } = new(null, null);
    public ICommand ApplyComparisonSelectionCommand => new RelayCommand(_ =>
    {
        if (PlanA == null || PlanB == null || ManualScopeA == null || ManualScopeB == null
            || !ComparisonScopesA.Contains(ManualScopeA) || !ComparisonScopesB.Contains(ManualScopeB)) return;
        ComparisonSelection = new(ManualScopeA.Key, ManualScopeB.Key);
        ComparisonSelectionRevision++;
        OnPropertyChanged(nameof(ComparisonSelectionRevision));
    }, _ => PlanA != null && PlanB != null && ManualScopeA != null && ManualScopeB != null);
    public ICommand ResetComparisonSelectionCommand => new RelayCommand(_ =>
    {
        ComparisonSelection = new(null, null);
        ManualScopeA = null; ManualScopeB = null;
        ComparisonSelectionRevision++;
        OnPropertyChanged(nameof(ComparisonSelectionRevision));
    });

    public void SetComparisonPlans(PlanSnapshot? planA, PlanSnapshot? planB, PlanComparisonSelection? selection = null)
    {
        // Publish a complete pair before notifications, including when A and B share one snapshot.
        _planA = planA; _planB = planB;
        ComparisonSelection = selection ?? new(planA?.SelectedQueryPlan, planB?.SelectedQueryPlan);
        ManualScopeA = null; ManualScopeB = null;
        OnPropertyChanged(nameof(PlanA)); OnPropertyChanged(nameof(PlanB));
        OnPropertyChanged(nameof(CompareVisible));
        OnPropertyChanged(nameof(CostDeltaText)); OnPropertyChanged(nameof(CostDeltaColor));
    }

    internal void PublishComparison(PlanComparisonResult? result, string? error = null)
    {
        var selectedA = ManualScopeA?.Key; var selectedB = ManualScopeB?.Key;
        _comparison = result; _comparisonError = error;
        OnPropertyChanged(nameof(Comparison));
        OnPropertyChanged(nameof(ComparisonScopesA)); OnPropertyChanged(nameof(ComparisonScopesB));
        ManualScopeA = ComparisonScopesA.FirstOrDefault(s => s.Key == selectedA)
            ?? ComparisonScopesA.FirstOrDefault(s => s.Key == ComparisonSelection.A);
        ManualScopeB = ComparisonScopesB.FirstOrDefault(s => s.Key == selectedB)
            ?? ComparisonScopesB.FirstOrDefault(s => s.Key == ComparisonSelection.B);
        OnPropertyChanged(nameof(ComparisonSummary)); OnPropertyChanged(nameof(ComparisonConditions));
        OnPropertyChanged(nameof(ComparisonUnmatchedA)); OnPropertyChanged(nameof(ComparisonUnmatchedB));
        OnPropertyChanged(nameof(CostDeltaText)); OnPropertyChanged(nameof(CostDeltaColor));
    }
}
