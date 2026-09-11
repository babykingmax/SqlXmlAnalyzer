using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class WorkspacePanelLayoutServiceTests
    {
        [Theory]
        [InlineData(GridUnitType.Pixel, 800, 30, double.PositiveInfinity, 0, 800)]
        [InlineData(GridUnitType.Pixel, 950, 30, double.PositiveInfinity, 280, 950)]
        [InlineData(GridUnitType.Pixel, 800, 30, 600, 800, 600)]
        [InlineData(GridUnitType.Pixel, 10, 30, 600, 10, 30)]
        [InlineData(GridUnitType.Auto, 1, 30, double.PositiveInfinity, 28, 30)]
        [InlineData(GridUnitType.Auto, 1, 30, double.PositiveInfinity, 350, 350)]
        [InlineData(GridUnitType.Auto, 1, 30, 300, 350, 300)]
        [InlineData(GridUnitType.Star, 1, 320, double.PositiveInfinity, 900, 320)]
        public void MinimumColumnWidth_AccountsForRestoredPixelsAndAutoWithoutLockingStarSize(
            GridUnitType unit, double value, double min, double max, double desired, double expected) =>
            WorkspacePanelLayoutService.MinimumColumnWidth(new GridLength(value, unit), min, max, desired).Should().Be(expected);

        [Fact]
        public void ToggleSqlCompare_WhenOriginalSqlIsCollapsed_ExpandsOriginalSqlPanel()
        {
            var service = new WorkspacePanelLayoutService();

            SqlComparePanelLayout layout = service.ToggleSqlCompare(new GridLength(0));

            layout.OriginalSqlWidth.GridUnitType.Should().Be(GridUnitType.Star);
            layout.OriginalSqlWidth.Value.Should().Be(1);
            layout.SplitterWidth.Value.Should().Be(4);
            layout.SplitterVisibility.Should().Be(Visibility.Visible);
            layout.ButtonContent.Should().Be("隐藏原始 SQL");
        }

        [Fact]
        public void ToggleSqlCompare_WhenOriginalSqlIsVisible_CollapsesOriginalSqlPanel()
        {
            var service = new WorkspacePanelLayoutService();

            SqlComparePanelLayout layout = service.ToggleSqlCompare(new GridLength(1, GridUnitType.Star));

            layout.OriginalSqlWidth.Value.Should().Be(0);
            layout.SplitterWidth.Value.Should().Be(0);
            layout.SplitterVisibility.Should().Be(Visibility.Collapsed);
            layout.ButtonContent.Should().Be("显示原始 SQL");
        }

        [Fact]
        public void ToggleDeadlockLeftPanel_TogglesBetweenDefaultWidthAndCollapsed()
        {
            var service = new WorkspacePanelLayoutService();

            SidePanelLayout collapsed = service.ToggleDeadlockLeftPanel(new GridLength(280));
            SidePanelLayout expanded = service.ToggleDeadlockLeftPanel(new GridLength(0));

            collapsed.Width.Value.Should().Be(0);
            collapsed.ButtonContent.Should().Be("▶ 侧边栏");
            expanded.Width.Value.Should().Be(280);
            expanded.ButtonContent.Should().Be("◀ 侧边栏");
        }

        [Fact]
        public void ToggleDeadlockRightPanel_TogglesBetweenDefaultWidthAndCollapsed()
        {
            var service = new WorkspacePanelLayoutService();

            SidePanelLayout collapsed = service.ToggleDeadlockRightPanel(new GridLength(320));
            SidePanelLayout expanded = service.ToggleDeadlockRightPanel(new GridLength(0));

            collapsed.Width.Value.Should().Be(0);
            collapsed.ButtonContent.Should().Be("◀ 属性栏");
            expanded.Width.Value.Should().Be(320);
            expanded.ButtonContent.Should().Be("属性栏 ▶");
        }

        [Fact]
        public void CollapseCollapsiblePanel_StoresCurrentWidthAndAppliesAuto()
        {
            var service = new WorkspacePanelLayoutService();
            GridLength currentWidth = new(320);

            CollapsiblePanelLayout layout = service.CollapseCollapsiblePanel(currentWidth);

            layout.StoredWidth.Should().Be(currentWidth);
            layout.AppliedWidth.Should().Be(GridLength.Auto);
        }

        [Fact]
        public void ExpandCollapsiblePanel_ReturnsStoredWidth()
        {
            var service = new WorkspacePanelLayoutService();
            GridLength storedWidth = new(280);

            GridLength width = service.ExpandCollapsiblePanel(storedWidth);

            width.Should().Be(storedWidth);
        }
    }
}
