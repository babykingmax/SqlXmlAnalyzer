using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Simulation;

/// <summary>Describes captured cost exposure, not a new execution plan or saved time.</summary>
public static class CostImpactSimulator
{
    public static CostImpactResult Simulate(XDocument? originalPlan, MissingIndexSuggestion proposedIndex, XNamespace ns,
        IUnexpectedErrorReporter? unexpectedErrors = null, CancellationToken cancellationToken = default) =>
        SimulateCandidates(originalPlan, new[] { proposedIndex }, ns, unexpectedErrors, cancellationToken);

    public static CostImpactResult SimulateCandidates(XDocument? originalPlan, IEnumerable<MissingIndexSuggestion> candidates,
        XNamespace ns, IUnexpectedErrorReporter? unexpectedErrors = null, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(candidates);
            ArgumentNullException.ThrowIfNull(ns);
            var inputs = candidates.Select(candidate => { cancellationToken.ThrowIfCancellationRequested(); return candidate; }).ToArray();
            var model = PlanIdentityAdapter.GetDocument(originalPlan, cancellationToken);
            if (model == null || model.Operators.Count == 0)
            {
                Logger.Warning("IMP-16: SIMULATION_INPUT_MISSING；未取得可评估的捕获算子。");
                return new(null, 0, 0, Array.Empty<SimulationCandidateResult>(), MissingCost(), MissingCost(),
                    "SIMULATION_INPUT_MISSING", "无足够数据评估成本影响；收益预测为 N/A。");
            }
            // Phase 1 freezes the full scope and own-cost denominator. Never add
            // statement totals or parent subtree totals on top of operator own costs.
            var operators = model.Operators.ToArray();
            var total = SumCosts(operators);
            var union = new HashSet<PlanOperatorKey>();
            var results = new List<SimulationCandidateResult>();
            bool unresolved = false;
            // Phase 2 uses that same denominator for every candidate, with overlap deduplicated.
            foreach (var input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (input == null) throw new InvalidDataException("SIMULATION_CANDIDATE_INVALID: 索引候选不能为空。");
                var target = IndexDdlCompiler.ResolveTarget(input);
                string[] ReadColumns(List<IndexColumn>? columns) => columns?.Select(column =>
                {
                    if (column == null) throw new InvalidDataException("SIMULATION_COLUMN_INVALID: 列定义无效。");
                    string name = IndexDdlCompiler.DecodeName(column.Name);
                    IndexDdlCompiler.QuoteIdentifier(name);
                    return name;
                }).ToArray() ?? throw new InvalidDataException("SIMULATION_COLUMNS_MISSING: 列定义缺失。");
                var keys = ReadColumns(input.KeyColumns);
                var includes = ReadColumns(input.IncludeColumns);
                string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { target, keys, includes }))));
                var matches = keys.Length == 0 ? Array.Empty<PlanOperator>() : IndexTargetResolver.FindOperators(input, originalPlan, ns)
                    .Select(source => model.FindOperator(source)!).DistinctBy(op => op.Key).ToArray();
                bool bound = matches.Length > 0;
                unresolved |= !bound;
                foreach (var op in matches) union.Add(op.Key);
                results.Add(new(identity, target, input.Location, Array.AsReadOnly(keys), Array.AsReadOnly(includes), bound ? SumCosts(matches) : MissingCost(),
                    Array.AsReadOnly(matches.Select(op => op.Key).ToArray()), bound ? "RELATED_ACCESS_OPERATORS" : "SIMULATION_TARGET_UNRESOLVED"));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var related = unresolved || inputs.Length == 0 ? MissingCost() : SumCosts(operators.Where(op => union.Contains(op.Key)));
            string status = unresolved || inputs.Length == 0 ? "SIMULATION_TARGET_UNRESOLVED"
                : !total.IsAvailable || !related.IsAvailable ? "SIMULATION_COSTS_INCOMPLETE" : "CAPTURED_COST_EXPOSURE";
            if (status != "CAPTURED_COST_EXPOSURE") Logger.Warning("IMP-16: 模拟输入证据不完整；不生成数值收益预测。");
            Logger.Debug($"IMP-16: 成本范围已汇总；算子 {operators.Length}，候选 {results.Count}，关联并集 {union.Count}。");
            return new(model.Envelope.DocumentId, model.QueryPlans.Count, operators.Length,
                Array.AsReadOnly(results.OrderBy(r => r.CandidateId, StringComparer.Ordinal).ThenBy(r => r.Location?.DisplayScope, StringComparer.Ordinal).ToArray()),
                total, related, status, $"已核对 {results.Count} 个候选，关联 {union.Count} 个目标访问算子；只描述原计划成本，不预测索引收益。");
        }
        catch (Exception exception)
        {
            ExceptionPolicy.Describe(exception, "CostImpactSimulator.SimulateCandidates", unexpectedErrors);
            throw;
        }
    }

    private static PlanMetric<double> MissingCost() => new(null, PlanMetricState.Missing, "optimizer-cost", "Captured RelOp own cost",
        PlanMetricKind.Estimated, PlanMetricAggregation.SumOperators);

    private static PlanMetric<double> SumCosts(IEnumerable<PlanOperator> operators)
    {
        var costs = operators.Select(op => op.Facts?.OwnCost).ToArray();
        if (costs.Any(c => c?.IsAvailable != true)) return MissingCost() with { State = PlanMetricState.Incomplete };
        double value = costs.Select(c => c!.Value!.Value).OrderBy(v => v).Sum();
        return double.IsFinite(value) ? MissingCost() with { Value = value, State = PlanMetricState.Available }
            : MissingCost() with { State = PlanMetricState.Invalid };
    }
}
