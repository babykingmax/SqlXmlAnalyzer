namespace SqlXmlAnalyzer.Core.Services;

public enum PlanWorkspaceLayoutMode { Wide, Medium, Narrow }
public enum PlanWorkspacePane { Graph, Issues, Details }

public sealed record PlanWorkspaceLayout(
    PlanWorkspaceLayoutMode Mode, double LeftWidth, double RightWidth,
    bool ShowGraph, bool ShowIssues, bool ShowDetails)
{
    public double LeftSplitterWidth => ShowGraph && ShowIssues ? 4 : 0;
    public double RightSplitterWidth => ShowGraph && ShowDetails ? 4 : 0;
}

/// <summary>Pure layout policy shared by resizing, navigation and persisted preferences.</summary>
public static class PlanWorkspaceLayoutService
{
    public static PlanWorkspaceLayout Calculate(double width, bool leftOpen = true, bool rightOpen = true,
        PlanWorkspacePane activePane = PlanWorkspacePane.Graph, double leftWidth = 280, double rightWidth = 320)
    {
        width = double.IsFinite(width) ? Math.Max(0, width) : 0;
        leftWidth = double.IsFinite(leftWidth) ? Math.Clamp(leftWidth, 220, 480) : 280;
        rightWidth = double.IsFinite(rightWidth) ? Math.Clamp(rightWidth, 260, 520) : 320;
        if (width < 1100)
            return new(PlanWorkspaceLayoutMode.Narrow,
                activePane == PlanWorkspacePane.Issues ? width : 0,
                activePane == PlanWorkspacePane.Details ? width : 0,
                activePane == PlanWorkspacePane.Graph,
                activePane == PlanWorkspacePane.Issues,
                activePane == PlanWorkspacePane.Details);

        bool showLeft = leftOpen;
        bool showRight = rightOpen;
        if (width < 1440 && showLeft && showRight)
        {
            showRight = activePane == PlanWorkspacePane.Details;
            showLeft = !showRight;
        }
        return new(width >= 1440 ? PlanWorkspaceLayoutMode.Wide : PlanWorkspaceLayoutMode.Medium,
            showLeft ? leftWidth : 0, showRight ? rightWidth : 0, true, showLeft, showRight);
    }

    public static double AuxiliaryHeight(double requested, double workspaceHeight) =>
        Math.Clamp(double.IsFinite(requested) ? requested : 220, 80,
            Math.Max(80, Math.Min(600, Math.Max(0, workspaceHeight) * 0.35)));
}
