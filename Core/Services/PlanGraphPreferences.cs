namespace SqlXmlAnalyzer.Core.Services;

public sealed record PlanGraphPreferences
{
    public DiagramViewMode ViewMode { get; init; } = DiagramViewMode.CostPercent;
    public bool HasExplicitViewMode { get; init; }
    public PlanLayoutMode LayoutMode { get; init; } = PlanLayoutMode.Horizontal;
    public PlanColorMode ColorMode { get; init; } = PlanColorMode.TotalCost;
    public LinkMetricMode LinkMetric { get; init; } = LinkMetricMode.RowCount;
    public bool ShowConnectionLabels { get; init; } = true;
}
