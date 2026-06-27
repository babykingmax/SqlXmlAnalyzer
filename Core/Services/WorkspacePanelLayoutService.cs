using System.Windows;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record SqlComparePanelLayout(
        GridLength OriginalSqlWidth,
        GridLength SplitterWidth,
        Visibility SplitterVisibility,
        string ButtonContent);

    public sealed record SidePanelLayout(
        GridLength Width,
        string ButtonContent);

    public sealed record CollapsiblePanelLayout(
        GridLength StoredWidth,
        GridLength AppliedWidth);

    public sealed class WorkspacePanelLayoutService
    {
        public SqlComparePanelLayout ToggleSqlCompare(GridLength currentOriginalSqlWidth)
        {
            bool isCollapsed = currentOriginalSqlWidth.Value == 0;

            return isCollapsed
                ? new SqlComparePanelLayout(
                    new GridLength(1, GridUnitType.Star),
                    new GridLength(4),
                    Visibility.Visible,
                    "隐藏原始 SQL")
                : new SqlComparePanelLayout(
                    new GridLength(0),
                    new GridLength(0),
                    Visibility.Collapsed,
                    "显示原始 SQL");
        }

        public SidePanelLayout ToggleDeadlockLeftPanel(GridLength currentWidth)
        {
            return currentWidth.Value > 0
                ? new SidePanelLayout(new GridLength(0), "▶ 侧边栏")
                : new SidePanelLayout(new GridLength(280), "◀ 侧边栏");
        }

        public SidePanelLayout ToggleDeadlockRightPanel(GridLength currentWidth)
        {
            return currentWidth.Value > 0
                ? new SidePanelLayout(new GridLength(0), "◀ 属性栏")
                : new SidePanelLayout(new GridLength(320), "属性栏 ▶");
        }

        public GridLength ExpandCollapsiblePanel(GridLength storedWidth)
        {
            return storedWidth;
        }

        public CollapsiblePanelLayout CollapseCollapsiblePanel(GridLength currentWidth)
        {
            return new CollapsiblePanelLayout(currentWidth, GridLength.Auto);
        }
    }
}
