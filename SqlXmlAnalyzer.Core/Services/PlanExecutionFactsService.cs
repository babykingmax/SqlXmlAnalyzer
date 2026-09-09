using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record PlanExecutionFacts(string? ActualExecutionMode, string? EstimatedExecutionMode,
    bool? Parallel, bool? Ordered)
{
    public string ExecutionMode => ActualExecutionMode
        ?? (EstimatedExecutionMode is { } estimate ? $"{estimate} (估算)" : "N/A");
    public string ParallelDisplay => Parallel?.ToString() ?? "N/A";
    public string OrderedDisplay => Ordered?.ToString() ?? "N/A";
}

public interface IPlanExecutionFactsReader
{
    PlanExecutionFacts Read(XElement relOp, XNamespace ns);
}

/// <summary>Reads only the current operator's Showplan facts. Missing data is never inferred from names or children.</summary>
public sealed class PlanExecutionFactsService : IPlanExecutionFactsReader
{
    public PlanExecutionFacts Read(XElement relOp, XNamespace ns)
    {
        ArgumentNullException.ThrowIfNull(relOp);
        ArgumentNullException.ThrowIfNull(ns);
        string? estimated = ReadMode(relOp.Attribute("EstimatedExecutionMode"));
        string?[] modes = relOp.Elements(ns + "RunTimeInformation")
            .Elements(ns + "RunTimeCountersPerThread")
            .Select(counter => ReadMode(counter.Attribute("ActualExecutionMode"))).ToArray();
        string? actual = null;
        if (modes.Length > 0 && modes.All(mode => mode != null))
        {
            string?[] distinct = modes.Distinct(StringComparer.Ordinal).OrderBy(mode => mode, StringComparer.Ordinal).ToArray();
            actual = distinct.Length == 1 ? distinct[0] : $"Mixed ({string.Join(", ", distinct)})";
        }

        // These are the Showplan types with Ordered; do not read InternalInfo, Sort or descendant RelOps.
        XElement[] scans = relOp.Elements().Where(element => element.Name.Namespace == ns
            && element.Name.LocalName is "IndexScan" or "TableScan" or "XcsScan").ToArray();
        bool? ordered = scans.Length == 1 ? ReadBoolean(scans[0].Attribute("Ordered")) : null;
        if (scans.Length > 1) Logger.Warning("IMP-07: 多个扫描节点，Ordered 保持未知。");
        Logger.Debug("IMP-07: 已读取当前算子的执行属性。");
        return new(actual, estimated, ReadBoolean(relOp.Attribute("Parallel")), ordered);
    }

    private static string? ReadMode(XAttribute? attribute)
    {
        if (attribute == null) return null;
        if (attribute.Value is "Row" or "Batch") return attribute.Value;
        Logger.Warning($"IMP-07: {attribute.Name.LocalName} 格式非法，保持未知。");
        return null;
    }

    private static bool? ReadBoolean(XAttribute? attribute)
    {
        if (attribute == null) return null;
        switch (attribute.Value.Trim(' ', '\t', '\r', '\n'))
        {
            case "true": case "1": return true;
            case "false": case "0": return false;
            default:
                Logger.Warning($"IMP-07: {attribute.Name.LocalName} 格式非法，保持未知。");
                return null;
        }
    }
}
