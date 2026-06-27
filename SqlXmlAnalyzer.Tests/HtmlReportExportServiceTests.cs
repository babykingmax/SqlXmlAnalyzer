using System;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class HtmlReportExportServiceTests
    {
        [Fact]
        public void Export_WhenSaveDialogIsCancelled_DoesNotWriteReport()
        {
            var dialogService = new FakeFileDialogService(null);
            var writer = new FakeHtmlReportWriter();
            var service = new HtmlReportExportService(writer, dialogService);

            HtmlReportExportResult result = service.Export(
                new HtmlReportExportRequest(CreateReport()));

            result.Status.Should().Be(HtmlReportExportStatus.Cancelled);
            result.OutputPath.Should().BeNull();
            writer.CallCount.Should().Be(0);
            dialogService.LastSaveRequest.Should().NotBeNull();
            dialogService.LastSaveRequest!.Filter.Should().Be("HTML report (*.html)|*.html");
            dialogService.LastSaveRequest.DefaultExtension.Should().Be(".html");
        }

        [Fact]
        public void Export_WhenPathIsSelected_WritesHtmlReport()
        {
            var dialogService = new FakeFileDialogService(@"C:\Reports\plan.html");
            var writer = new FakeHtmlReportWriter(@"C:\Reports\plan.html");
            var service = new HtmlReportExportService(writer, dialogService);
            HtmlAnalysisReport report = CreateReport();

            HtmlReportExportResult result = service.Export(
                new HtmlReportExportRequest(report));

            result.Status.Should().Be(HtmlReportExportStatus.Exported);
            result.OutputPath.Should().Be(@"C:\Reports\plan.html");
            writer.CallCount.Should().Be(1);
            writer.Report.Should().BeSameAs(report);
            writer.OutputPath.Should().Be(@"C:\Reports\plan.html");
            dialogService.LastSaveRequest!.Title.Should().Be("Save ExecutionPlan analysis report");
            dialogService.LastSaveRequest.FileName.Should().Be("ExecutionPlanReport_sample.html");
        }

        [Fact]
        public void Export_WhenWriterReturnsFallbackPath_ReturnsSavedPath()
        {
            var dialogService = new FakeFileDialogService(@"C:\Reports\plan.html");
            var writer = new FakeHtmlReportWriter(@"C:\Fallback\fallback.html");
            var service = new HtmlReportExportService(writer, dialogService);

            HtmlReportExportResult result = service.Export(
                new HtmlReportExportRequest(CreateReport()));

            result.Status.Should().Be(HtmlReportExportStatus.Exported);
            result.OutputPath.Should().Be(@"C:\Fallback\fallback.html");
        }

        private static HtmlAnalysisReport CreateReport()
        {
            return new HtmlAnalysisReport(
                "sample.sqlplan",
                "ExecutionPlan",
                "Summary",
                "flowchart TD",
                Array.Empty<HtmlReportSection>(),
                "ExecutionPlanReport_sample.html",
                Array.Empty<MissingIndexSuggestion>());
        }

        private sealed class FakeFileDialogService : IFileDialogService
        {
            private readonly string? _savePath;

            public FakeFileDialogService(string? savePath)
            {
                _savePath = savePath;
            }

            public FileDialogRequest? LastSaveRequest { get; private set; }

            public string? ShowOpenFile(FileDialogRequest request)
            {
                throw new NotSupportedException();
            }

            public string? ShowSaveFile(FileDialogRequest request)
            {
                LastSaveRequest = request;
                return _savePath;
            }
        }

        private sealed class FakeHtmlReportWriter : IHtmlReportWriter
        {
            private readonly string? _savedPath;

            public FakeHtmlReportWriter(string? savedPath = null)
            {
                _savedPath = savedPath;
            }

            public int CallCount { get; private set; }
            public HtmlAnalysisReport? Report { get; private set; }
            public string? OutputPath { get; private set; }

            public string Save(HtmlAnalysisReport report, string outputPath)
            {
                CallCount++;
                Report = report;
                OutputPath = outputPath;
                return _savedPath ?? outputPath;
            }
        }
    }
}
