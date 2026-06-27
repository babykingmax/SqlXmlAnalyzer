using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class AnalysisClipboardServiceTests
    {
        [Fact]
        public void BuildForTab_WhenDeadlockDiagnosticsExist_ReturnsDeadlockReportText()
        {
            var service = new AnalysisClipboardService();

            AnalysisClipboardResult result = service.BuildForTab(
                selectedTabIndex: 0,
                deadlockDiagnostics: "Deadlock details",
                planDiagnostics: "Plan details");

            result.Status.Should().Be(AnalysisClipboardStatus.Ready);
            result.Text.Should().Be(
                "=== SQL Server 死锁诊断报告 ===\r\n\r\nDeadlock details");
            result.UserMessage.Should().BeNull();
        }

        [Fact]
        public void BuildForTab_WhenPlanDiagnosticsExist_ReturnsPlanReportText()
        {
            var service = new AnalysisClipboardService();

            AnalysisClipboardResult result = service.BuildForTab(
                selectedTabIndex: 1,
                deadlockDiagnostics: "Deadlock details",
                planDiagnostics: "Plan details");

            result.Status.Should().Be(AnalysisClipboardStatus.Ready);
            result.Text.Should().Be(
                "=== SQL Server 执行计划诊断报告 ===\r\n\r\nPlan details");
            result.UserMessage.Should().BeNull();
        }

        [Fact]
        public void BuildForTab_WhenDeadlockDiagnosticsAreEmpty_ReturnsEmptyMessage()
        {
            var service = new AnalysisClipboardService();

            AnalysisClipboardResult result = service.BuildForTab(
                selectedTabIndex: 0,
                deadlockDiagnostics: " ",
                planDiagnostics: "Plan details");

            result.Status.Should().Be(AnalysisClipboardStatus.Empty);
            result.Text.Should().BeEmpty();
            result.UserMessage.Should().Be("当前没有死锁诊断结果可复制！");
        }

        [Fact]
        public void BuildForTab_WhenPlanDiagnosticsAreEmpty_ReturnsEmptyMessage()
        {
            var service = new AnalysisClipboardService();

            AnalysisClipboardResult result = service.BuildForTab(
                selectedTabIndex: 1,
                deadlockDiagnostics: "Deadlock details",
                planDiagnostics: null);

            result.Status.Should().Be(AnalysisClipboardStatus.Empty);
            result.Text.Should().BeEmpty();
            result.UserMessage.Should().Be("当前没有执行计划诊断结果可复制！");
        }

        [Fact]
        public void BuildForTab_WhenTabIsUnsupported_ReturnsUnsupported()
        {
            var service = new AnalysisClipboardService();

            AnalysisClipboardResult result = service.BuildForTab(
                selectedTabIndex: 2,
                deadlockDiagnostics: "Deadlock details",
                planDiagnostics: "Plan details");

            result.Status.Should().Be(AnalysisClipboardStatus.UnsupportedTab);
            result.Text.Should().BeEmpty();
            result.UserMessage.Should().BeNull();
        }

        [Fact]
        public void BuildRefactoredSql_WhenSqlExists_ReturnsReadyText()
        {
            var service = new AnalysisClipboardService();

            AnalysisClipboardResult result =
                service.BuildRefactoredSql("SELECT 1;");

            result.Status.Should().Be(AnalysisClipboardStatus.Ready);
            result.Text.Should().Be("SELECT 1;");
            result.UserMessage.Should().BeNull();
        }

        [Fact]
        public void BuildRefactoredSql_WhenSqlIsEmpty_ReturnsEmptyMessage()
        {
            var service = new AnalysisClipboardService();

            AnalysisClipboardResult result = service.BuildRefactoredSql("");

            result.Status.Should().Be(AnalysisClipboardStatus.Empty);
            result.Text.Should().BeEmpty();
            result.UserMessage.Should().Be("当前没有重构后的 SQL 可复制！");
        }
    }
}
