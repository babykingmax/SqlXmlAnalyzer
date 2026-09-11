using System.Xml.Linq;
using System.IO;
using SqlXmlAnalyzer.Core.Comparison;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Core.Services;

public enum PlanComparisonNodeState { Unchanged, Added, Removed, OperatorChanged }
public sealed record RuntimeMetricDelta(string Label, double? Value, double? Delta)
{
    public double? OtherValue { get; init; }
    public double? PercentDelta { get; init; }
    public string Source { get; init; } = "";
    public string Reason { get; init; } = "";
}
public sealed record PlanComparisonNode(XElement Source, string PhysicalOp, string? OtherPhysicalOp,
    double? Cost, double? OtherCost, double? CostPercentDelta, PlanComparisonNodeState State,
    IReadOnlyList<RuntimeMetricDelta> RuntimeDeltas, IReadOnlyList<PlanComparisonNode> Children)
{
    public PlanOperatorKey? Identity { get; init; }
    public PlanOperatorKey? OtherIdentity { get; init; }
    public double? CostDelta { get; init; }
    public string MatchEvidence { get; init; } = "";
    public ComparisonConfidence Confidence { get; init; }
    public string CostReason { get; init; } = "";
}
public sealed record ComparisonScopeOption(PlanQueryPlanKey Key, string Label);
public sealed record PlanComparisonSelection(PlanQueryPlanKey? A, PlanQueryPlanKey? B);
public sealed record StatementComparisonResult(ComparisonStatement? A, ComparisonStatement? B,
    ComparisonConfidence Confidence, string MatchReason, ComparisonQuery? QueryA, ComparisonQuery? QueryB,
    ComparisonConditions Conditions, IReadOnlyList<PlanComparisonNode> RootsA, IReadOnlyList<PlanComparisonNode> RootsB)
{
    public string Label => $"A: {Scope(A, QueryA)} ↔ B: {Scope(B, QueryB)} [{Confidence}]";
    public string Detail => MatchReason + "。" + Conditions.Summary;
    private static string Scope(ComparisonStatement? s, ComparisonQuery? q) => s == null ? "未匹配" :
        $"B{s.Statement.Key.Batch.BatchOrdinal}/S{s.Statement.Key.StatementOrdinal}" + (q == null ? "（无 QueryPlan）" : $"/Q{q.Plan.Key.QueryPlanOrdinal}");
}
public sealed record PlanComparisonResult(PlanComparisonNode? PlanA, PlanComparisonNode? PlanB)
{
    public IReadOnlyList<StatementComparisonResult> Statements { get; init; } = [];
    public IReadOnlyList<ComparisonScopeOption> ScopesA { get; init; } = [];
    public IReadOnlyList<ComparisonScopeOption> ScopesB { get; init; } = [];
    public string ModelVersion => PlanComparisonEvidence.Version;
    public string Summary => $"语句/计划配对 {Statements.Count(s => s.A != null && s.B != null && s.QueryA != null && s.QueryB != null)}；"
        + $"A 未匹配 {Statements.Count(s => s.A != null && s.B == null || s.QueryA != null && s.QueryB == null)}；"
        + $"B 未匹配 {Statements.Count(s => s.B != null && s.A == null || s.QueryB != null && s.QueryA == null)}；"
        + $"待确认 {Statements.Count(s => s.Confidence == ComparisonConfidence.Candidate)}。按语句查看差值，未汇总不可比成本。";
}

public sealed class PlanComparisonController
{
    private readonly IPlanComparisonRuntimeMetricsReader _runtimeMetrics;
    private readonly IUnexpectedErrorReporter? _unexpectedErrors;
    public PlanComparisonController(IPlanComparisonRuntimeMetricsReader? runtimeMetrics = null, IUnexpectedErrorReporter? unexpectedErrors = null)
    { _runtimeMetrics = runtimeMetrics ?? new PlanComparisonRuntimeMetricsService(); _unexpectedErrors = unexpectedErrors; }

    public PlanComparisonResult BuildComparison(PlanSnapshot? planA, PlanSnapshot? planB, XNamespace fallbackShowplanNamespace,
        CancellationToken cancellationToken = default, PlanComparisonSelection? selection = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(fallbackShowplanNamespace);
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ComparisonStatement> Read(PlanSnapshot? snapshot) => PlanIdentityAdapter.GetDocument(snapshot?.Document, cancellationToken) is { } model
                ? PlanComparisonEvidence.Read(snapshot!.Document, model, cancellationToken: cancellationToken,
                    originalSourceHash: snapshot.CapturedOriginalSourceHash,
                    recordedOriginalSourceHash: PlanSnapshot.NormalizeHash(snapshot.OriginalSourceHash)) : [];
            var allA = Read(planA); var allB = Read(planB);
            var selectedA = selection == null ? planA?.SelectedQueryPlan : selection.A;
            var selectedB = selection == null ? planB?.SelectedQueryPlan : selection.B;
            IReadOnlyList<ComparisonStatement> Select(IReadOnlyList<ComparisonStatement> all, PlanQueryPlanKey? selected)
            {
                if (selected == null) return all;
                var statement = all.SingleOrDefault(s => s.Statement.Key == selected.Statement);
                var query = statement?.Queries.SingleOrDefault(q => q.Plan.Key == selected);
                if (statement == null || query == null) throw new InvalidDataException("PLAN_SELECTION_NOT_FOUND: 比较选择不属于当前源快照，请重新选择。");
                return [statement with { Queries = new[] { query } }];
            }
            var statements = new List<StatementComparisonResult>();
            var pairs = PlanComparisonEvidence.MatchStatements(Select(allA, selectedA), Select(allB, selectedB),
                selectedA != null && selectedB != null, cancellationToken);
            foreach (var pair in pairs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var queryPairs = PlanComparisonEvidence.MatchQueries(pair, cancellationToken);
                if (queryPairs.Count == 0)
                    statements.Add(new(pair.A, pair.B, pair.Confidence, pair.Reason, null, null,
                        new(false, false, ["语句没有可比较的 QueryPlan"]), [], []));
                foreach (var queryPair in queryPairs)
                {
                    var confidence = pair.Confidence == ComparisonConfidence.High ? queryPair.Confidence : pair.Confidence;
                    var conditions = PlanComparisonEvidence.Assess(queryPair.A, queryPair.B, confidence);
                    var operatorPairs = PlanComparisonEvidence.MatchOperators(queryPair.A, queryPair.B, cancellationToken);
                    var nodesA = BuildNodes(queryPair.A, queryPair.B, planA, planB, operatorPairs, conditions, false, cancellationToken);
                    var nodesB = BuildNodes(queryPair.B, queryPair.A, planB, planA, operatorPairs, conditions, true, cancellationToken);
                    statements.Add(new(pair.A, pair.B, confidence, pair.Reason + "；" + queryPair.Reason,
                        queryPair.A, queryPair.B, conditions, nodesA, nodesB));
                }
            }
            // A damaged/legacy document with unscoped RelOps is displayed, but never auto-paired.
            var legacyA = allA.Count == 0 ? Legacy(planA, false, cancellationToken) : null;
            var legacyB = allB.Count == 0 ? Legacy(planB, true, cancellationToken) : null;
            var rootsA = statements.SelectMany(s => s.RootsA).ToArray(); var rootsB = statements.SelectMany(s => s.RootsB).ToArray();
            var result = new PlanComparisonResult(rootsA.Length == 1 ? rootsA[0] : legacyA, rootsB.Length == 1 ? rootsB[0] : legacyB)
            {
                Statements = statements.AsReadOnly(), ScopesA = Scopes(allA), ScopesB = Scopes(allB)
            };
            Logger.Debug($"IMP-17: 比较展示已生成；A 语句 {allA.Count}，B 语句 {allB.Count}，结果 {statements.Count}。");
            if (statements.Any(s => !s.Conditions.EstimatesComparable || !s.Conditions.RuntimeComparable
                || s.QueryA?.IncompleteStructureOperators.Count > 0 || s.QueryB?.IncompleteStructureOperators.Count > 0))
                Logger.Warning("IMP-17: 部分对象、算子结构或采集条件不足，对应指标差值保持未知。");
            return result;
        }
        catch (Exception ex) { ExceptionPolicy.Describe(ex, "PlanComparisonController.BuildComparison", _unexpectedErrors); throw; }
    }

    private static IReadOnlyList<ComparisonScopeOption> Scopes(IReadOnlyList<ComparisonStatement> statements) => statements
        .SelectMany(s => s.Queries.Select(q => new ComparisonScopeOption(q.Plan.Key,
            $"B{s.Statement.Key.Batch.BatchOrdinal}/S{s.Statement.Key.StatementOrdinal}/Q{q.Plan.Key.QueryPlanOrdinal} — {s.Statement.Text ?? s.Statement.Kind}"))).ToArray();

    private IReadOnlyList<PlanComparisonNode> BuildNodes(ComparisonQuery? current, ComparisonQuery? other,
        PlanSnapshot? snapshot, PlanSnapshot? otherSnapshot, IReadOnlyList<EvidencePair<PlanOperator>> pairs,
        ComparisonConditions conditions, bool isB, CancellationToken token)
    {
        if (current == null) return [];
        var matches = pairs.Where(p => (isB ? p.B : p.A) != null).ToDictionary(p => (isB ? p.B : p.A)!.Key);
        var children = current.Plan.Operators.Where(o => o.Parent != null).ToLookup(o => o.Parent!);
        var nodes = new Dictionary<PlanOperatorKey, PlanComparisonNode>();
        foreach (var op in current.Plan.Operators.Reverse())
        {
            token.ThrowIfCancellationRequested();
            var pair = matches[op.Key]; var matched = isB ? pair.A : pair.B;
            var element = snapshot!.IdentityModel!.GetOperatorSource(op.Key)!;
            var otherElement = matched == null ? null : otherSnapshot!.IdentityModel!.GetOperatorSource(matched.Key);
            var cost = op.Facts?.SubtreeCost.Value; var otherCost = matched?.Facts?.SubtreeCost.Value;
            // A changed physical operation is only a correspondence candidate, not a comparable measurement.
            bool comparable = pair.Confidence == ComparisonConfidence.High;
            double? delta = comparable && conditions.EstimatesComparable ? Difference(cost, otherCost) : null;
            nodes.Add(op.Key, new(element, op.PhysicalOp ?? "Unknown", matched?.PhysicalOp, cost, otherCost,
                Percent(delta, otherCost), matched == null ? (isB ? PlanComparisonNodeState.Added : PlanComparisonNodeState.Removed)
                : op.PhysicalOp == matched.PhysicalOp ? PlanComparisonNodeState.Unchanged : PlanComparisonNodeState.OperatorChanged,
                Runtime(element, otherElement, comparable && conditions.RuntimeComparable), children[op.Key].Select(c => nodes[c.Key]).ToArray())
            {
                Identity = op.Key, OtherIdentity = matched?.Key, CostDelta = delta, Confidence = pair.Confidence,
                MatchEvidence = pair.Reason, CostReason = delta == null ? "匹配、成本或采集条件不足，差值 N/A" : "捕获的估算子树成本差值，非耗时变化"
            });
        }
        return current.Plan.Operators.Where(o => o.Parent == null).Select(o => nodes[o.Key]).ToArray();
    }

    private IReadOnlyList<RuntimeMetricDelta> Runtime(XElement current, XElement? other, bool comparable)
    {
        var a = _runtimeMetrics.Read(current); var b = other == null ? new PlanComparisonRuntimeMetrics(null, null, null) : _runtimeMetrics.Read(other);
        var factsA = PlanOperatorFactsService.Get(current, current.Name.Namespace);
        var factsB = other == null ? null : PlanOperatorFactsService.Get(other, other.Name.Namespace);
        bool sameExecutions = factsA.LogicalExecutions.Value is { } executions && factsB?.LogicalExecutions.Value == executions
            && factsA.Execution.Parallel.HasValue && factsA.Execution.Parallel == factsB.Execution.Parallel
            && factsA.Execution.ActualExecutionMode != null && factsA.Execution.ActualExecutionMode == factsB.Execution.ActualExecutionMode;
        RuntimeMetricDelta Metric(string label, double? value, double? otherValue, string source)
        {
            value = Finite(value); otherValue = Finite(otherValue);
            double? delta = comparable && sameExecutions ? Difference(value, otherValue) : null;
            return new(label, value, delta) { OtherValue = otherValue, PercentDelta = Percent(delta, otherValue), Source = source,
                Reason = delta == null ? "指标缺失或匹配/采集/执行口径不可比" : "捕获运行记录；非独立性能实验" };
        }
        var result = new List<RuntimeMetricDelta> { Metric("Elapsed", a.Elapsed, b.Elapsed, "RunTimeCountersPerThread.ActualElapsedms；ms；MaxThreads"),
            Metric("Logical reads", a.LogicalReads, b.LogicalReads, "RunTimeCountersPerThread.ActualLogicalReads；页；SumThreads") };
        if (a.RowsRead.HasValue || b.RowsRead.HasValue) result.Add(Metric("Rows read", a.RowsRead, b.RowsRead, "RunTimeCountersPerThread.ActualRowsRead；行；SumThreads"));
        return result.AsReadOnly();
    }

    private PlanComparisonNode? Legacy(PlanSnapshot? snapshot, bool isB, CancellationToken token)
    {
        if (snapshot?.Document.Root == null) return null;
        var ns = snapshot.Document.Root.Name.Namespace;
        var operators = snapshot.Document.Descendants(ns + "RelOp").ToArray();
        if (operators.Length == 0) return null;
        var nodes = new Dictionary<XElement, PlanComparisonNode>();
        foreach (var element in operators.AsEnumerable().Reverse())
        {
            token.ThrowIfCancellationRequested();
            nodes[element] = new(element, (string?)element.Attribute("PhysicalOp") ?? "Unknown", null,
                PlanOperatorFactsService.Get(element, ns).SubtreeCost.Value, null, null,
                isB ? PlanComparisonNodeState.Added : PlanComparisonNodeState.Removed, Runtime(element, null, false),
                PlanDiagnosticAnalyzer.GetDirectChildRelOps(element, ns).Select(c => nodes[c]).ToArray()) { MatchEvidence = "无语句身份；未匹配" };
        }
        return nodes[operators[0]];
    }
    internal static double? Finite(double? value) => value.HasValue && double.IsFinite(value.Value) && value >= 0 ? value : null;
    internal static double? Difference(double? value, double? other) => Finite(value) is { } a && Finite(other) is { } b && double.IsFinite(a - b) ? a - b : null;
    internal static double? Percent(double? delta, double? baseline) => delta.HasValue && baseline > 0 && double.IsFinite(delta.Value / baseline.Value * 100)
        ? delta.Value / baseline.Value * 100 : null;
}
