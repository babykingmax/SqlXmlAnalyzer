using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Utilities;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class HtmlReportActionServiceTests
    {
        private static readonly XNamespace ShowplanNs =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void BuildReport_WhenDeadlockDocumentExists_ReturnsDeadlockReport()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "deadlock_bookmark_lookup.xdl");
            var document = XDocument.Parse(xml);
            var service = new HtmlReportActionService();

            HtmlReportActionResult result = service.BuildReport(
                selectedTabIndex: 0,
                deadlockDocument: document,
                deadlockFilePath: "C:\\Temp\\deadlock.xdl",
                deadlockDetailText: "detail",
                planDocument: null,
                planFilePath: null,
                showplanNamespace: ShowplanNs);

            result.Status.Should().Be(HtmlReportActionStatus.Ready);
            result.Report.Should().NotBeNull();
            result.Report!.AnalysisType.Should().Be("Deadlock");
            result.Report.SummaryText.Should().Contain("deadlock.xdl");
        }

        [Fact]
        public void BuildReport_WhenPlanDocumentExists_ReturnsExecutionPlanReport()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "plan_missing_index.sqlplan");
            var document = XDocument.Parse(xml);
            var service = new HtmlReportActionService();

            HtmlReportActionResult result = service.BuildReport(
                selectedTabIndex: 1,
                deadlockDocument: null,
                deadlockFilePath: null,
                deadlockDetailText: "",
                planDocument: document,
                planFilePath: "C:\\Temp\\plan.sqlplan",
                showplanNamespace: ShowplanNs);

            result.Status.Should().Be(HtmlReportActionStatus.Ready);
            result.Report.Should().NotBeNull();
            result.Report!.AnalysisType.Should().Be("ExecutionPlan");
            result.Report.SummaryText.Should().Contain("plan.sqlplan");
        }

        [Fact]
        public void BuildReport_WhenDeadlockDocumentIsMissing_ReturnsMissingDocument()
        {
            var service = new HtmlReportActionService();

            HtmlReportActionResult result = service.BuildReport(
                selectedTabIndex: 0,
                deadlockDocument: null,
                deadlockFilePath: null,
                deadlockDetailText: "",
                planDocument: null,
                planFilePath: null,
                showplanNamespace: ShowplanNs);

            result.Status.Should().Be(HtmlReportActionStatus.MissingDocument);
            result.Report.Should().BeNull();
            result.UserMessage.Should().Be("Please open and analyze a deadlock XML file first.");
        }

        [Fact]
        public void BuildReport_WhenPlanDocumentIsMissing_ReturnsMissingDocument()
        {
            var service = new HtmlReportActionService();

            HtmlReportActionResult result = service.BuildReport(
                selectedTabIndex: 1,
                deadlockDocument: null,
                deadlockFilePath: null,
                deadlockDetailText: "",
                planDocument: null,
                planFilePath: null,
                showplanNamespace: ShowplanNs);

            result.Status.Should().Be(HtmlReportActionStatus.MissingDocument);
            result.Report.Should().BeNull();
            result.UserMessage.Should().Be("Please open and analyze an execution plan file first.");
        }

        [Fact]
        public void BuildReport_WhenTabIsUnsupported_ReturnsUnsupportedTab()
        {
            var service = new HtmlReportActionService();

            HtmlReportActionResult result = service.BuildReport(
                selectedTabIndex: 99,
                deadlockDocument: null,
                deadlockFilePath: null,
                deadlockDetailText: "",
                planDocument: null,
                planFilePath: null,
                showplanNamespace: ShowplanNs);

            result.Status.Should().Be(HtmlReportActionStatus.UnsupportedTab);
            result.Report.Should().BeNull();
            result.UserMessage.Should().Be("There is no selected analysis tab.");
        }
    }
}
