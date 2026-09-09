using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record PlanGraphRelOpDetails(string ObjectDetails, string DatabaseName, string TableName,
    string IndexName, IReadOnlyList<string> Predicates, IReadOnlyList<string> SeekPredicates,
    IReadOnlyList<string> OutputColumns, bool IsPartitioned, string PartitionCount, string PartitionRange);

public sealed class PlanGraphRelOpDetailsService
{
    public PlanGraphRelOpDetails Parse(XElement relOp, XNamespace ns, string? physicalOp)
    {
        var facts = PlanOperatorFactsService.Get(relOp, ns);
        var obj = facts.Objects.Count == 1 ? facts.Objects[0] : null;
        string Display(Models.PlanObjectFacts item) => item.DisplayName +
            (item.Part("Alias") is { } alias && alias != item.Part("Table") ? $" AS {Models.SqlObjectIdentity.Quote(alias)}" : "");
        return new(string.Join("; ", facts.Objects.Select(Display)), obj?.Part("Database") ?? "",
            obj?.Part("Table") ?? "", obj?.Part("Index") ?? "", facts.Predicates, facts.SeekPredicates,
            facts.OutputColumns, facts.Partitioned == true, facts.PartitionCount ?? "", facts.PartitionRange ?? "");
    }
}
