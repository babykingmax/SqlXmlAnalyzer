using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanWorkspaceResponsiveLayoutTests
{
    [Theory]
    [InlineData(1440)]
    [InlineData(1920)]
    public void Wide_DefaultKeepsBothSidebarsAtTheirPreferredWidths(double width)
    {
        var layout = PlanWorkspaceLayoutService.Calculate(width);
        layout.Mode.Should().Be(PlanWorkspaceLayoutMode.Wide);
        layout.LeftWidth.Should().Be(280);
        layout.RightWidth.Should().Be(320);
        layout.ShowGraph.Should().BeTrue();
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1439)]
    public void Medium_OnlyTheActiveSidebarCanShareTheGraph(double width)
    {
        var initial = PlanWorkspaceLayoutService.Calculate(width);
        initial.ShowIssues.Should().BeTrue();
        initial.ShowDetails.Should().BeFalse();
        var details = PlanWorkspaceLayoutService.Calculate(width, activePane: PlanWorkspacePane.Details);
        details.ShowIssues.Should().BeFalse();
        details.ShowDetails.Should().BeTrue();
        details.ShowGraph.Should().BeTrue();
    }

    [Theory]
    [InlineData(PlanWorkspacePane.Graph, true, false, false)]
    [InlineData(PlanWorkspacePane.Issues, false, true, false)]
    [InlineData(PlanWorkspacePane.Details, false, false, true)]
    public void Narrow_ShowsOnlyTheRequestedPane(PlanWorkspacePane pane, bool graph, bool issues, bool details)
    {
        var layout = PlanWorkspaceLayoutService.Calculate(1099, activePane: pane);
        layout.ShowGraph.Should().Be(graph);
        layout.ShowIssues.Should().Be(issues);
        layout.ShowDetails.Should().Be(details);
        layout.LeftSplitterWidth.Should().Be(0);
        layout.RightSplitterWidth.Should().Be(0);
    }

    [Fact]
    public void NarrowRoundTrip_DoesNotAlterUserPreferredWidths()
    {
        PlanWorkspaceLayoutService.Calculate(900, activePane: PlanWorkspacePane.Issues, leftWidth: 350);
        var restored = PlanWorkspaceLayoutService.Calculate(1600, leftWidth: 350, rightWidth: 410);
        restored.LeftWidth.Should().Be(350);
        restored.RightWidth.Should().Be(410);
    }

    [Fact]
    public void AuxiliaryPanel_IsBoundedAfterWindowBecomesShorter()
    {
        PlanWorkspaceLayoutService.AuxiliaryHeight(500, 600).Should().Be(210);
        PlanWorkspaceLayoutService.AuxiliaryHeight(double.NaN, 800).Should().Be(220);
    }
}
