using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Comparison;

internal static class PlanComparisonObjectEvidence
{
    internal static bool IsComplete(PlanOperator op)
    {
        // Local Showplan usually omits Server. When captured, ObjectSet still compares it exactly.
        // An absent Object on a base-table access is missing evidence, unlike a Constant Scan/spool.
        if (op.Objects.Count == 0 && (RequiresObject(op.PhysicalOp) || RequiresObject(op.LogicalOp))) return false;
        return op.Objects.All(o => !string.IsNullOrWhiteSpace(o.Identity.Database)
            && !string.IsNullOrWhiteSpace(o.Identity.Schema) && !string.IsNullOrWhiteSpace(o.Identity.Object));
    }

    private static bool RequiresObject(string? operation) => operation is
        "Table Scan" or "Index Scan" or "Clustered Index Scan" or "Index Seek" or "Clustered Index Seek"
        or "Columnstore Index Scan" or "Clustered Columnstore Index Scan" or "Table-valued function"
        or "Table Insert" or "Index Insert" or "Clustered Index Insert" or "Columnstore Index Insert"
        or "Table Update" or "Index Update" or "Clustered Index Update"
        or "Table Delete" or "Index Delete" or "Clustered Index Delete"
        or "Table Merge" or "Index Merge" or "Clustered Index Merge"
        || operation?.StartsWith("Remote ", StringComparison.Ordinal) == true;
}
