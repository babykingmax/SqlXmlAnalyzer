using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services;

/// <summary>Extracts operator-local facts once per source revision for legacy XML consumers.</summary>
public sealed class PlanOperatorFactsService(IPlanExecutionFactsReader? executionFacts = null,
    IUnexpectedErrorReporter? unexpectedErrors = null)
{
    private static readonly ConditionalWeakTable<XElement, CacheEntry> Cache = new();

    public static PlanOperatorFacts Get(XElement relOp, XNamespace ns, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relOp);
        ArgumentNullException.ThrowIfNull(ns);
        if (relOp.Name != ns + "RelOp") throw new InvalidDataException("PLAN_FACTS_EXPECTED_RELOP: 需要相同命名空间的 RelOp。");
        cancellationToken.ThrowIfCancellationRequested();
        return Cache.GetValue(relOp, element => new CacheEntry(element)).Get(cancellationToken);
    }

    public PlanOperatorFacts Read(XElement relOp, XNamespace ns, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relOp);
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (relOp.Name != ns + "RelOp") throw new InvalidDataException("PLAN_FACTS_EXPECTED_RELOP: 需要相同命名空间的 RelOp。");
            var payload = Visit(relOp, ns, includeChildren: true, cancellationToken).ToArray();
            var local = payload.Where(e => e.Name.LocalName != "RelOp").ToArray();
            var localSet = local.ToHashSet();
            bool Within(XElement element, params string[] names) => element.Ancestors().TakeWhile(a => a != relOp)
                .Any(a => localSet.Contains(a) && names.Contains(a.Name.LocalName));
            IReadOnlyList<string> Scalars(params string[] names) => Array.AsReadOnly(local
                .Where(e => e.Name.LocalName == "ScalarOperator" && Within(e, names)
                    && !Within(e, "Warnings", "RunTimeInformation", "OutputList"))
                .Select(e => (string?)e.Attribute("ScalarString")).OfType<string>()
                .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray());
            var objects = Array.AsReadOnly(local.Where(e => e.Name.LocalName == "Object"
                    && !Within(e, "Warnings", "RunTimeInformation", "OutputList"))
                .Select(e => new PlanObjectFacts(Attributes(e))).ToArray());
            var threads = Array.AsReadOnly(local.Where(e => e.Name.LocalName == "RunTimeCountersPerThread"
                && e.Parent?.Name == ns + "RunTimeInformation" && e.Parent.Parent == relOp)
                .Select(ReadThread).ToArray());
            bool duplicateThreads = threads.Where(t => t.ThreadId.HasValue).GroupBy(t => t.ThreadId).Any(g => g.Count() > 1);
            var workers = threads.Where(t => t.ThreadId > 0).ToArray();
            PlanWorkerRowDistribution? workerRows = !duplicateThreads && workers.Length > 0
                && threads.All(t => t.ThreadId is >= 0) && workers.All(t => t.OutputRows.IsAvailable)
                ? new(workers.Length, workers.Sum(t => t.OutputRows.Value!.Value), workers.Max(t => t.OutputRows.Value!.Value),
                    workers.Average(t => t.OutputRows.Value!.Value)) : null;
            PlanMetric<decimal> Total(Func<PlanThreadFacts, PlanMetric<decimal>> select, string source, string unit) =>
                Sum(threads.Select(select).ToArray(), source, unit, duplicateThreads);
            var output = Total(t => t.OutputRows, "RunTimeInformation/RunTimeCountersPerThread/@ActualRows", "rows");
            var read = Total(t => t.RowsRead, "RunTimeInformation/RunTimeCountersPerThread/@ActualRowsRead", "rows");
            var executions = Total(t => t.Executions, "RunTimeInformation/RunTimeCountersPerThread/@ActualExecutions", "executions");
            var elapsed = Total(t => t.ElapsedTime, "RunTimeInformation/RunTimeCountersPerThread/@ActualElapsedms", "ms");
            elapsed = elapsed with { Value = elapsed.IsAvailable ? threads.Max(t => t.ElapsedTime.Value) : null,
                Aggregation = PlanMetricAggregation.MaxThreads };
            var logical = LogicalExecutions(threads, duplicateThreads);
            var perExecution = new PlanMetric<decimal>(output.IsAvailable && logical.Value > 0
                ? output.Value / logical.Value : null,
                !output.IsAvailable ? output.State : logical.Value > 0 ? PlanMetricState.Available : PlanMetricState.Ambiguous,
                "rows/execution", "ActualRows / logical executions", PlanMetricKind.Actual, PlanMetricAggregation.RowsPerExecution);
            var subtree = Estimate(relOp, "EstimatedTotalSubtreeCost", "optimizer-cost");
            var childCosts = payload.Where(e => e.Name.LocalName == "RelOp")
                .Select(e => Estimate(e, "EstimatedTotalSubtreeCost", "optimizer-cost")).ToArray();
            var own = new PlanMetric<double>(null, subtree.IsAvailable ? PlanMetricState.Incomplete : subtree.State,
                "optimizer-cost", "EstimatedTotalSubtreeCost - direct child subtree costs", PlanMetricKind.Estimated,
                PlanMetricAggregation.SubtreeMinusChildren);
            if (subtree.IsAvailable && childCosts.All(c => c.IsAvailable))
            {
                double value = subtree.Value!.Value - childCosts.Select(c => c.Value!.Value).OrderBy(v => v).Sum();
                // Rounded Showplan costs can differ by a few floating-point ulps.
                own = double.IsFinite(value) && value >= -1e-9 * Math.Max(1, subtree.Value.Value)
                    ? own with { Value = Math.Max(0, value), State = PlanMetricState.Available }
                    : own with { State = PlanMetricState.Invalid };
            }
            var rebinds = Estimate(relOp, "EstimateRebinds", "executions");
            var rewinds = Estimate(relOp, "EstimateRewinds", "executions");
            double? estimatedExecutions = rebinds.IsAvailable && rewinds.IsAvailable ? rebinds.Value + rewinds.Value + 1 : null;
            var estimatedExecs = new PlanMetric<double>(estimatedExecutions is { } e && double.IsFinite(e) ? e : null,
                estimatedExecutions is { } v ? double.IsFinite(v) ? PlanMetricState.Available : PlanMetricState.Invalid
                    : rebinds.State == PlanMetricState.Missing && rewinds.State == PlanMetricState.Missing ? PlanMetricState.Missing : PlanMetricState.Incomplete,
                "executions", "EstimateRebinds + EstimateRewinds + 1", PlanMetricKind.Estimated, PlanMetricAggregation.RebindsPlusRewindsPlusOne);
            XElement? partition = local.FirstOrDefault(e => e.Name.LocalName == "PartitionsAccessed");
            XElement? range = local.FirstOrDefault(e => e.Name.LocalName == "PartitionRange" && e.Parent == partition);
            string? start = (string?)range?.Attribute("Start"), end = (string?)range?.Attribute("End");
            bool? partitioned = partition != null ? true : local.Prepend(relOp).Select(e => (string?)e.Attribute("Partitioned"))
                .Where(v => v != null).Select(v => v?.Trim() switch { "true" or "1" => (bool?)true, "false" or "0" => false, _ => null })
                .FirstOrDefault();
            var result = new PlanOperatorFacts(objects, Scalars("Predicate", "ProbeResidual", "Residual"),
                Scalars("SeekPredicates", "SeekPredicate", "SeekPredicateNew"),
                Array.AsReadOnly(local.Where(e => e.Name.LocalName == "ColumnReference" && Within(e, "OutputList"))
                    .Select(e => (string?)e.Attribute("Column")).OfType<string>().Distinct(StringComparer.Ordinal).ToArray()),
                partitioned, (string?)partition?.Attribute("PartitionCount"), start == null ? null : end == null ? start : $"{start} - {end}",
                (executionFacts ?? new PlanExecutionFactsService()).Read(relOp, ns), threads,
                Estimate(relOp, "EstimateRows", "rows/execution"), Estimate(relOp, "EstimatedRowsRead", "rows/execution"),
                Estimate(relOp, "EstimateCPU", "optimizer-cost"), Estimate(relOp, "EstimateIO", "optimizer-cost"),
                Estimate(relOp, "AvgRowSize", "bytes/row"), estimatedExecs, subtree, own, output, read, executions, logical, perExecution,
                Total(t => t.Rebinds, "RunTimeInformation/RunTimeCountersPerThread/@ActualRebinds", "rebinds"),
                Total(t => t.Rewinds, "RunTimeInformation/RunTimeCountersPerThread/@ActualRewinds", "rewinds"), elapsed,
                Total(t => t.CpuTime, "RunTimeInformation/RunTimeCountersPerThread/@ActualCPUms", "ms"),
                Total(t => t.LogicalReads, "RunTimeInformation/RunTimeCountersPerThread/@ActualLogicalReads", "pages"),
                Total(t => t.PhysicalReads, "RunTimeInformation/RunTimeCountersPerThread/@ActualPhysicalReads", "pages"))
            {
                PhysicalOp = (string?)relOp.Attribute("PhysicalOp"),
                HasResidualPredicate = local.Any(e => e.Name.LocalName is "Predicate" or "ProbeResidual" or "Residual"
                    && !Within(e, "Warnings", "RunTimeInformation", "OutputList")),
                WorkerRows = workerRows,
                ScalarExpressions = Array.AsReadOnly(local.Where(e => e.Name.LocalName == "ScalarOperator"
                        && !Within(e, "Warnings", "RunTimeInformation"))
                    .Select(e => (string?)e.Attribute("ScalarString")).OfType<string>()
                    .Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToArray())
            };
            cancellationToken.ThrowIfCancellationRequested();
            Logger.Debug($"IMP-11: 已提取本算子事实；线程数 {threads.Count}，输出行状态 {output.State}，单次行数状态 {perExecution.State}。");
            return result;
        }
        catch (Exception exception)
        {
            ExceptionPolicy.Describe(exception, "PlanOperatorFactsService.Read", unexpectedErrors);
            throw;
        }
    }

    public static IEnumerable<XElement> LocalElements(XElement relOp, XNamespace ns, CancellationToken cancellationToken = default) =>
        Visit(relOp, ns, includeChildren: false, cancellationToken);

    private static IEnumerable<XElement> Visit(XElement relOp, XNamespace ns, bool includeChildren, CancellationToken cancellationToken)
    {
        var pending = new Stack<XElement>(relOp.Elements().Reverse());
        while (pending.TryPop(out var element))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element.Name.Namespace != ns || element.Name.LocalName is "InternalInfo" or "Statements" or "QueryPlan") continue;
            if (element.Name.LocalName == "RelOp")
            {
                if (includeChildren) yield return element;
                continue;
            }
            yield return element;
            foreach (var child in element.Elements().Reverse()) pending.Push(child);
        }
    }

    private static IReadOnlyDictionary<string, string> Attributes(XElement element) =>
        new ReadOnlyDictionary<string, string>(element.Attributes().ToDictionary(a => a.Name.ToString(), a => a.Value));

    private static PlanThreadFacts ReadThread(XElement element)
    {
        XAttribute? thread = element.Attribute("Thread");
        // Showplan declares Thread as xsd:int: normalize signed decimal spellings for
        // identity comparisons, trim XML whitespace only, and retain the source text below.
        int? id = thread != null && int.TryParse(thread.Value.AsSpan().Trim(" \t\r\n"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : null;
        if (thread != null && id == null) Logger.Warning("IMP-11: Thread 格式非法，线程角色保持未知。");
        else if (id < 0) Logger.Warning("IMP-11: Thread 编号为负，线程角色保持未知。");
        return new(id, Attributes(element), Counter(element, "ActualRows", "rows"), Counter(element, "ActualRowsRead", "rows"),
            Counter(element, "ActualExecutions", "executions"), Counter(element, "ActualRebinds", "rebinds"), Counter(element, "ActualRewinds", "rewinds"),
            Counter(element, "ActualElapsedms", "ms"), Counter(element, "ActualCPUms", "ms"),
            Counter(element, "ActualLogicalReads", "pages"), Counter(element, "ActualPhysicalReads", "pages"));
    }

    private static PlanMetric<decimal> Counter(XElement element, string name, string unit)
    {
        XAttribute? attribute = element.Attribute(name);
        bool valid = ulong.TryParse(attribute?.Value.Trim(' ', '\t', '\r', '\n'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ulong value);
        if (attribute != null && !valid) Logger.Warning($"IMP-11: {name} 非法，指标保持未知。");
        return new(valid ? value : null, valid ? PlanMetricState.Available : attribute == null ? PlanMetricState.Missing : PlanMetricState.Invalid,
            unit, $"RunTimeInformation/RunTimeCountersPerThread/@{name}", PlanMetricKind.Actual, PlanMetricAggregation.Attribute);
    }

    private static PlanMetric<double> Estimate(XElement element, string name, string unit)
    {
        XAttribute? attribute = element.Attribute(name);
        bool valid = double.TryParse(attribute?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) && value >= 0;
        if (attribute != null && !valid) Logger.Warning($"IMP-11: {name} 非法，指标保持未知。");
        return new(valid ? value : null, valid ? PlanMetricState.Available : attribute == null ? PlanMetricState.Missing : PlanMetricState.Invalid,
            unit, $"RelOp/@{name}", PlanMetricKind.Estimated, PlanMetricAggregation.Attribute);
    }

    private static PlanMetric<decimal> Sum(PlanMetric<decimal>[] metrics, string source, string unit, bool duplicate)
    {
        var state = duplicate ? PlanMetricState.Ambiguous : metrics.Length == 0 || metrics.All(m => m.State == PlanMetricState.Missing)
            ? PlanMetricState.Missing : metrics.All(m => m.IsAvailable) ? PlanMetricState.Available : PlanMetricState.Incomplete;
        return new(state == PlanMetricState.Available ? metrics.Sum(m => m.Value!.Value) : null, state, unit, source,
            PlanMetricKind.Actual, PlanMetricAggregation.SumThreads);
    }

    private static PlanMetric<decimal> LogicalExecutions(IReadOnlyList<PlanThreadFacts> threads, bool duplicate)
    {
        var unknown = new PlanMetric<decimal>(null, threads.Count == 0 ? PlanMetricState.Missing : PlanMetricState.Ambiguous,
            "executions", "serial counter or common active worker executions", PlanMetricKind.Actual, PlanMetricAggregation.CommonWorkerExecutions);
        // Signed int values are preserved, but only 0 and positive IDs have a defined
        // coordinator/worker role in this aggregation contract. Do not ignore negative IDs.
        if (duplicate || threads.Any(t => !t.OutputRows.IsAvailable || !t.Executions.IsAvailable
            || t.SourceAttributes.ContainsKey("Thread") && t.ThreadId is null or < 0)) return unknown;
        if (threads.Count == 1) return unknown with { Value = threads[0].Executions.Value, State = PlanMetricState.Available };
        if (threads.Any(t => t.ThreadId == null)) return unknown;
        // In parallel regions thread 0 is the coordinator. Nonzero output there plus workers
        // does not establish a shared execution denominator; preserve totals but abstain.
        if (threads.Any(t => t.ThreadId == 0 && t.OutputRows.Value != 0)) return unknown;
        var workers = threads.Where(t => t.ThreadId > 0).ToArray();
        if (workers.Any(t => t.Executions.Value == 0 && t.OutputRows.Value != 0)) return unknown;
        var counts = workers.Where(t => t.Executions.Value > 0).Select(t => t.Executions.Value).Distinct().ToArray();
        return counts.Length == 1 ? unknown with { Value = counts[0], State = PlanMetricState.Available } : unknown;
    }

    private sealed class CacheEntry
    {
        private readonly XElement _source;
        private readonly object _gate = new();
        private PlanOperatorFacts? _facts;
        public CacheEntry(XElement source)
        {
            _source = source;
            source.Changed += (_, _) => { lock (_gate) _facts = null; };
        }
        public PlanOperatorFacts Get(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _facts ??= new PlanOperatorFactsService().Read(_source, _source.Name.Namespace, cancellationToken);
            }
        }
    }
}
