using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlXmlAnalyzer.Services;

internal sealed record PlanGraphBoundary(PlanNodeViewModel VisibleNode, PlanNodeViewModel Destination, string Direction)
{
    public string Label => $"#{VisibleNode.NodeId} {Direction} #{Destination.NodeId} · {Destination.PhysicalOp}";
}

internal static class PlanGraphPresentationService
{
    public static IReadOnlyList<PlanNodeViewModel> Search(IEnumerable<PlanNodeViewModel> nodes, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<PlanNodeViewModel>();
        string term = query.Trim();
        return nodes.Where(n => n.NodeId.Equals(term.TrimStart('#'), StringComparison.OrdinalIgnoreCase)
            || n.PhysicalOp.Contains(term, StringComparison.OrdinalIgnoreCase)
            || n.ObjectDisplay.Contains(term, StringComparison.OrdinalIgnoreCase)
            || n.ObjectDetails.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public static IReadOnlyList<PlanGraphBoundary> Boundaries(IEnumerable<ConnectionViewModel> connections,
        IReadOnlyCollection<PlanNodeViewModel> page)
    {
        var included = page.ToHashSet();
        return connections.Where(c => c.Source != null && c.Target != null && c.IsVisible
                && included.Contains(c.Source) != included.Contains(c.Target))
            .Select(c => included.Contains(c.Source!)
                ? new PlanGraphBoundary(c.Source!, c.Target!, "输出到")
                : new PlanGraphBoundary(c.Target!, c.Source!, "输入来自"))
            .ToArray();
    }
}
