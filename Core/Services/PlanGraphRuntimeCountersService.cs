using System.Globalization;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record PlanGraphRuntimeCountersResult(bool HasActual, bool HasActualRead,
    double ActualRows, double ActualRowsRead, double ActualExecutions, double ActualRebinds,
    double ActualRewinds, bool IsThreadDataSkewed, decimal ExactActualRows, decimal ExactActualRowsRead)
{
    public PlanOperatorFacts? Facts { get; init; }
    public string ActualRowsDisplay => HasActual ? ExactActualRows.ToString("N0", CultureInfo.InvariantCulture) : "N/A";
    public string ActualRowsReadDisplay => HasActualRead ? ExactActualRowsRead.ToString("N0", CultureInfo.InvariantCulture) : "N/A";
}

public sealed class PlanGraphRuntimeCountersService
{
    public PlanGraphRuntimeCountersResult Parse(XElement relOp, XNamespace ns)
    {
        var facts = PlanOperatorFactsService.Get(relOp, ns);
        bool skew = facts.WorkerRows is { Count: > 1, Total: > 100 } workers && workers.Maximum > workers.Average * 2;
        return new(facts.OutputRows.IsAvailable, facts.RowsRead.IsAvailable,
            (double)(facts.OutputRows.Value ?? 0), (double)(facts.RowsRead.Value ?? 0),
            (double)(facts.ThreadExecutions.Value ?? 0), (double)(facts.Rebinds.Value ?? 0),
            (double)(facts.Rewinds.Value ?? 0), facts.OutputRows.IsAvailable && skew,
            facts.OutputRows.Value ?? 0, facts.RowsRead.Value ?? 0) { Facts = facts };
    }
}
