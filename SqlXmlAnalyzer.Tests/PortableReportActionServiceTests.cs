using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PortableReportActionServiceTests
    {
        [Fact]
        public void BuildReport_WhenDeadlockTabHasFile_ReturnsDeadlockReportWithDiagram()
        {
            var service = new PortableReportActionService();
            var patterns = new[]
            {
                new DeadlockPattern(
                    "Key Lookup Deadlock",
                    "High",
                    "Description",
                    "Cause",
                    "Recommendation")
            };

            PortableReportActionResult result = service.BuildReport(
                selectedTabIndex: 0,
                deadlockFilePath: "C:\\Temp\\deadlock.xdl",
                deadlockPatterns: patterns,
                deadlockDetailText: "detail",
                planFilePath: null,
                planDiagnosticsText: "",
                extension: "pdf");

            result.Status.Should().Be(PortableReportActionStatus.Ready);
            result.Report.Should().NotBeNull();
            result.Report!.DefaultFileName.Should().Be("DeadlockReport_deadlock.pdf");
            result.IncludeDeadlockDiagram.Should().BeTrue();
            result.Report.Content.Should().Contain("Key Lookup Deadlock");
        }

        [Fact]
        public void BuildReport_WhenPlanTabHasDiagnostics_ReturnsPlanReportWithoutDiagram()
        {
            var service = new PortableReportActionService();

            PortableReportActionResult result = service.BuildReport(
                selectedTabIndex: 1,
                deadlockFilePath: null,
                deadlockPatterns: null,
                deadlockDetailText: "",
                planFilePath: "C:\\Temp\\plan.sqlplan",
                planDiagnosticsText: "Plan diagnostics",
                extension: "docx");

            result.Status.Should().Be(PortableReportActionStatus.Ready);
            result.Report.Should().NotBeNull();
            result.Report!.DefaultFileName.Should().Be("PlanReport_plan.docx");
            result.IncludeDeadlockDiagram.Should().BeFalse();
            result.Report.Content.Should().Be("Plan diagnostics");
        }

        [Fact]
        public void BuildReport_WhenDeadlockFileIsMissing_ReturnsMissingContent()
        {
            var service = new PortableReportActionService();

            PortableReportActionResult result = service.BuildReport(
                selectedTabIndex: 0,
                deadlockFilePath: null,
                deadlockPatterns: null,
                deadlockDetailText: "",
                planFilePath: null,
                planDiagnosticsText: "",
                extension: "pdf");

            result.Status.Should().Be(PortableReportActionStatus.MissingContent);
            result.Report.Should().BeNull();
            result.UserMessage.Should().Be("There is no loaded deadlock document to export.");
        }

        [Fact]
        public void BuildReport_WhenPlanDiagnosticsAreMissing_ReturnsMissingContent()
        {
            var service = new PortableReportActionService();

            PortableReportActionResult result = service.BuildReport(
                selectedTabIndex: 1,
                deadlockFilePath: null,
                deadlockPatterns: null,
                deadlockDetailText: "",
                planFilePath: "C:\\Temp\\plan.sqlplan",
                planDiagnosticsText: "",
                extension: "pdf");

            result.Status.Should().Be(PortableReportActionStatus.MissingContent);
            result.Report.Should().BeNull();
            result.UserMessage.Should().Be("There are no execution plan diagnostics to export.");
        }

        [Fact]
        public void BuildReport_WhenTabIsUnsupported_ReturnsUnsupportedTab()
        {
            var service = new PortableReportActionService();

            PortableReportActionResult result = service.BuildReport(
                selectedTabIndex: 99,
                deadlockFilePath: null,
                deadlockPatterns: null,
                deadlockDetailText: "",
                planFilePath: null,
                planDiagnosticsText: "",
                extension: "pdf");

            result.Status.Should().Be(PortableReportActionStatus.UnsupportedTab);
            result.Report.Should().BeNull();
        }
    }
}
