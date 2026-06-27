using System;
using System.Windows;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PortableReportExportServiceTests
    {
        [Fact]
        public void Export_WhenSaveDialogIsCancelled_DoesNotExport()
        {
            var dialogService = new FakeFileDialogService(null);
            var exporter = new FakeReportExporter();
            var service = new PortableReportExportService(exporter, dialogService);

            PortableReportExportResult result = service.Export(CreateRequest("pdf"));

            result.Status.Should().Be(PortableReportExportStatus.Cancelled);
            result.OutputPath.Should().BeNull();
            exporter.CallCount.Should().Be(0);
            dialogService.LastSaveRequest.Should().NotBeNull();
            dialogService.LastSaveRequest!.DefaultExtension.Should().Be(".pdf");
        }

        [Fact]
        public void Export_WhenPathIsSelected_ExportsPortableReport()
        {
            var dialogService = new FakeFileDialogService(@"C:\Reports\deadlock.docx");
            var exporter = new FakeReportExporter();
            var service = new PortableReportExportService(exporter, dialogService);

            PortableReportExportResult result = service.Export(CreateRequest(".DOCX"));

            result.Status.Should().Be(PortableReportExportStatus.Exported);
            result.OutputPath.Should().Be(@"C:\Reports\deadlock.docx");
            exporter.CallCount.Should().Be(1);
            exporter.Extension.Should().Be("docx");
            exporter.OutputPath.Should().Be(@"C:\Reports\deadlock.docx");
            exporter.Title.Should().Be("Diagnostic Report");
            exporter.Content.Should().Be("Report content");
            dialogService.LastSaveRequest!.Title.Should().Be("Save DOCX analysis report");
            dialogService.LastSaveRequest.DefaultExtension.Should().Be(".docx");
            dialogService.LastSaveRequest.FileName.Should().Be("Report.docx");
        }

        [Fact]
        public void Export_WhenExtensionIsEmpty_Throws()
        {
            var service = new PortableReportExportService(
                new FakeReportExporter(),
                new FakeFileDialogService(@"C:\Reports\report.pdf"));

            Action act = () => service.Export(CreateRequest(" "));

            act.Should().Throw<ArgumentException>()
                .WithMessage("Report extension cannot be empty.*");
        }

        private static PortableReportExportRequest CreateRequest(string extension)
        {
            var report = new PortableAnalysisReport(
                "Diagnostic Report",
                "Report content",
                "Report.docx",
                IncludeDeadlockDiagram: false);

            return new PortableReportExportRequest(
                extension,
                "Word report (*.docx)|*.docx",
                report,
                ImageElement: null);
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

        private sealed class FakeReportExporter : IPdfWordReportExporter
        {
            public int CallCount { get; private set; }
            public string? Extension { get; private set; }
            public string? OutputPath { get; private set; }
            public string? Title { get; private set; }
            public string? Content { get; private set; }
            public FrameworkElement? ImageElement { get; private set; }

            public void Export(
                string extension,
                string outputPath,
                string title,
                string content,
                FrameworkElement? imageElement = null)
            {
                CallCount++;
                Extension = extension;
                OutputPath = outputPath;
                Title = title;
                Content = content;
                ImageElement = imageElement;
            }
        }
    }
}
