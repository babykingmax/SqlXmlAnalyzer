using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Utilities;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class AnalysisReportControllerTests
    {
        private static readonly XNamespace ShowplanNs =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void BuildDeadlockHtmlReport_ReturnsStructuredReport()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "deadlock_bookmark_lookup.xdl");
            var document = XDocument.Parse(xml);
            var controller = new AnalysisReportController();

            HtmlAnalysisReport report = controller.BuildDeadlockHtmlReport(
                document,
                "deadlock_bookmark_lookup.xdl",
                "Selected process detail");

            report.AnalysisType.Should().Be("Deadlock");
            report.DefaultFileName.Should().Be("DeadlockReport_deadlock_bookmark_lookup.html");
            report.SummaryText.Should().Contain("Deadlock file: deadlock_bookmark_lookup.xdl");
            report.MermaidCode.Should().Contain("flowchart TD");
            report.Sections.Should().ContainSingle();
            report.Sections[0].Items.Should().Contain(item =>
                item.Heading == "Selected item analysis");
        }

        [Fact]
        public void BuildPlanHtmlReport_ReturnsDiagnosticsAndMissingIndexes()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "plan_missing_index.sqlplan");
            var document = XDocument.Parse(xml);
            var controller = new AnalysisReportController();

            HtmlAnalysisReport report = controller.BuildPlanHtmlReport(
                document,
                "plan_missing_index.sqlplan",
                ShowplanNs);

            report.AnalysisType.Should().Be("ExecutionPlan");
            report.DefaultFileName.Should().Be("ExecutionPlanReport_plan_missing_index.html");
            report.SummaryText.Should().Contain("Execution plan file: plan_missing_index.sqlplan");
            report.MermaidCode.Should().Contain("flowchart TD");
            report.Sections.Should().ContainSingle();
            report.Sections[0].Items.Should().NotBeEmpty();
            report.MissingIndexes.Should().NotBeEmpty();
        }

        [Fact]
        public void BuildDeadlockPortableReport_RemovesUnsupportedGlyphs()
        {
            var controller = new AnalysisReportController();
            var patterns = new[]
            {
                new DeadlockPattern(
                    "Key Lookup Deadlock",
                    "High",
                    "Description",
                    "Cause",
                    "Recommendation")
            };

            PortableAnalysisReport report = controller.BuildDeadlockPortableReport(
                "deadlock.xdl",
                patterns,
                "Detail \U0001F4A1 text \U0001F50D",
                "pdf");

            report.Title.Should().Be("SQL Server Deadlock Diagnostic Report");
            report.DefaultFileName.Should().Be("DeadlockReport_deadlock.pdf");
            report.IncludeDeadlockDiagram.Should().BeTrue();
            report.Content.Should().Contain("Key Lookup Deadlock");
            report.Content.Should().Contain("Detail  text ");
            report.Content.Any(char.IsSurrogate).Should().BeFalse();
        }

        [Fact]
        public void BuildPlanPortableReport_ReturnsPlanReportMetadata()
        {
            var controller = new AnalysisReportController();

            PortableAnalysisReport report = controller.BuildPlanPortableReport(
                "sample.sqlplan",
                "Plan diagnostics",
                "docx");

            report.Title.Should().Be("SQL Server Execution Plan Diagnostic Report");
            report.Content.Should().Be("Plan diagnostics");
            report.DefaultFileName.Should().Be("PlanReport_sample.docx");
            report.IncludeDeadlockDiagram.Should().BeFalse();
        }
    }
}
