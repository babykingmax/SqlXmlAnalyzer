using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record PlanComparisonRuntimeMetrics(double? Elapsed, double? LogicalReads, double? RowsRead);

public interface IPlanComparisonRuntimeMetricsReader
{
    PlanComparisonRuntimeMetrics Read(XElement relOp);
}

public sealed class PlanComparisonRuntimeMetricsService : IPlanComparisonRuntimeMetricsReader
{
    public PlanComparisonRuntimeMetrics Read(XElement relOp)
    {
        ArgumentNullException.ThrowIfNull(relOp);
        var facts = PlanOperatorFactsService.Get(relOp, relOp.Name.Namespace);
        return new((double?)facts.ElapsedTime.Value, (double?)facts.LogicalReads.Value, (double?)facts.RowsRead.Value);
    }
}
