using System.Globalization;
using System.Text.Json.Serialization;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PlanMetricState>))]
public enum PlanMetricState { Available, Missing, Invalid, Incomplete, Ambiguous }
[JsonConverter(typeof(JsonStringEnumConverter<PlanMetricKind>))]
public enum PlanMetricKind { Estimated, Actual }
[JsonConverter(typeof(JsonStringEnumConverter<PlanMetricAggregation>))]
public enum PlanMetricAggregation { Attribute, SumThreads, MaxThreads, CommonWorkerExecutions, RowsPerExecution, RebindsPlusRewindsPlusOne, SubtreeMinusChildren, SumOperators }

/// <summary>A missing or unusable measurement never carries a fabricated zero.</summary>
public sealed record PlanMetric<T>(T? Value, PlanMetricState State, string Unit, string Source,
    PlanMetricKind Kind, PlanMetricAggregation Aggregation) where T : struct, IFormattable
{
    public bool IsPresent => State != PlanMetricState.Missing;
    public bool IsAvailable => State == PlanMetricState.Available && Value.HasValue;
    public string Display(string format = "G") => IsAvailable ? Value!.Value.ToString(format, CultureInfo.InvariantCulture) : "N/A";
}

public sealed record PlanThreadFacts(int? ThreadId, IReadOnlyDictionary<string, string> SourceAttributes,
    PlanMetric<decimal> OutputRows, PlanMetric<decimal> RowsRead, PlanMetric<decimal> Executions,
    PlanMetric<decimal> Rebinds, PlanMetric<decimal> Rewinds, PlanMetric<decimal> ElapsedTime,
    PlanMetric<decimal> CpuTime, PlanMetric<decimal> LogicalReads, PlanMetric<decimal> PhysicalReads);

public sealed record PlanWorkerRowDistribution(int Count, decimal Total, decimal Maximum, decimal Average);

public sealed record PlanObjectFacts(IReadOnlyDictionary<string, string> SourceAttributes)
{
    public string? Part(string name) => SqlObjectIdentity.DecodeIdentifier(SourceAttributes.GetValueOrDefault(name));
    public string DisplayName => Part("Table") is not { } table ? "" : SqlObjectIdentity.Quote(table)
        + (Part("Index") is { } index ? "." + SqlObjectIdentity.Quote(index) : "");
}

/// <summary>Immutable facts for one RelOp. Lists never include facts belonging to child operators.</summary>
public sealed record PlanOperatorFacts(
    IReadOnlyList<PlanObjectFacts> Objects, IReadOnlyList<string> Predicates,
    IReadOnlyList<string> SeekPredicates, IReadOnlyList<string> OutputColumns,
    bool? Partitioned, string? PartitionCount, string? PartitionRange,
    PlanExecutionFacts Execution, IReadOnlyList<PlanThreadFacts> Threads,
    PlanMetric<double> EstimatedRows, PlanMetric<double> EstimatedRowsRead,
    PlanMetric<double> EstimatedCpuCost, PlanMetric<double> EstimatedIoCost,
    PlanMetric<double> AverageRowSize, PlanMetric<double> EstimatedExecutions,
    PlanMetric<double> SubtreeCost, PlanMetric<double> OwnCost,
    PlanMetric<decimal> OutputRows, PlanMetric<decimal> RowsRead,
    PlanMetric<decimal> ThreadExecutions, PlanMetric<decimal> LogicalExecutions,
    PlanMetric<decimal> RowsPerExecution, PlanMetric<decimal> Rebinds, PlanMetric<decimal> Rewinds,
    PlanMetric<decimal> ElapsedTime, PlanMetric<decimal> CpuTime, PlanMetric<decimal> LogicalReads, PlanMetric<decimal> PhysicalReads)
{
    public string? PhysicalOp { get; init; }
    public bool HasResidualPredicate { get; init; }
    public IReadOnlyList<string> ScalarExpressions { get; init; } = Array.Empty<string>();
    public PlanWorkerRowDistribution? WorkerRows { get; init; }
    [JsonIgnore]
    public bool HasData => HasResidualPredicate || Objects.Count > 0 || ScalarExpressions.Count > 0 || Predicates.Count > 0 || SeekPredicates.Count > 0 || OutputColumns.Count > 0
        || Threads.Count > 0 || Partitioned.HasValue || Execution.Parallel.HasValue || Execution.Ordered.HasValue
        || Execution.ActualExecutionMode != null || Execution.EstimatedExecutionMode != null
        || EstimatedRows.IsPresent || EstimatedRowsRead.IsPresent || EstimatedCpuCost.IsPresent || EstimatedIoCost.IsPresent
        || AverageRowSize.IsPresent || EstimatedExecutions.IsPresent || SubtreeCost.IsPresent;
}
