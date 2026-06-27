using System;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphCollapseLogServiceTests
    {
        private static readonly DateTime Timestamp =
            new DateTime(2026, 6, 27, 9, 8, 7, 123);

        [Fact]
        public void BuildStartLine_WhenNodeIsExpanded_UsesCollapseAction()
        {
            var service = new PlanGraphCollapseLogService();
            var node = new PlanGraphCollapseLogNode("3", "Hash Match", IsCollapsed: false);

            string text = service.BuildStartLine(node, Timestamp);

            text.Should().Be(
                "\n[09:08:07.123] --- START CLICK: Collapse [-] on [3] Hash Match ---\n");
        }

        [Fact]
        public void BuildToggleLog_ReportsVisibleNodeAndConnectionDiffs()
        {
            var service = new PlanGraphCollapseLogService();
            var nodeBeforeToggle =
                new PlanGraphCollapseLogNode("0", "Nested Loops", IsCollapsed: true);
            var oldSnapshot = new PlanGraphCollapseLogSnapshot(
                new[]
                {
                    new PlanGraphCollapseLogNode("0", "Nested Loops", IsCollapsed: true),
                    new PlanGraphCollapseLogNode("1", "Index Scan", IsCollapsed: false)
                },
                new[]
                {
                    new PlanGraphCollapseLogConnection("1", "Index Scan", "0", "Nested Loops")
                });
            var newSnapshot = new PlanGraphCollapseLogSnapshot(
                new[]
                {
                    new PlanGraphCollapseLogNode("0", "Nested Loops", IsCollapsed: false),
                    new PlanGraphCollapseLogNode("2", "Key Lookup", IsCollapsed: false)
                },
                new[]
                {
                    new PlanGraphCollapseLogConnection("2", "Key Lookup", "0", "Nested Loops")
                });

            string text = service.BuildToggleLog(
                nodeBeforeToggle,
                newCollapsedState: false,
                oldSnapshot,
                newSnapshot,
                Timestamp);

            text.Should().Contain("[09:08:07.123] Action: Expand [+] on Node [0] Nested Loops");
            text.Should().Contain("[09:08:07.123] Toggled IsCollapsed to False");
            text.Should().Contain("[09:08:07.123] Nodes Added (Expanded): 1");
            text.Should().Contain("  + [2] Key Lookup (Collapsed State: False)");
            text.Should().Contain("[09:08:07.123] Nodes Removed (Hidden): 1");
            text.Should().Contain("  - [1] Index Scan");
            text.Should().Contain("[09:08:07.123] Connections Added: 1");
            text.Should().Contain("  + [2] Key Lookup --> [0] Nested Loops");
            text.Should().Contain("[09:08:07.123] Connections Removed: 1");
            text.Should().Contain("  - [1] Index Scan --> [0] Nested Loops");
            text.Should().NotContain("  + [0] Nested Loops");
            text.Should().NotContain("  - [0] Nested Loops");
        }

        [Fact]
        public void BuildExceptionLog_IncludesTimestampAndException()
        {
            var service = new PlanGraphCollapseLogService();
            var exception = new InvalidOperationException("bad state");

            string text = service.BuildExceptionLog(exception, Timestamp);

            text.Should().StartWith("\n[09:08:07.123] [EXCEPTION CAUGHT]: ");
            text.Should().Contain("System.InvalidOperationException: bad state");
        }
    }
}
