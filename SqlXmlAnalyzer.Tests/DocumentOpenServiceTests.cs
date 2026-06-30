using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DocumentOpenServiceTests : IDisposable
    {
        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer_DocumentOpen_{Guid.NewGuid():N}");
        private readonly DocumentOpenService _service = new();

        public DocumentOpenServiceTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public async Task OpenAsync_WhenFileIsDeadlockXml_ReturnsDeadlockKind()
        {
            string path = WriteFile("deadlock.xdl", "<deadlock><victim-list /></deadlock>");

            DocumentOpenResult result = await _service.OpenAsync(path);

            result.IsSuccess.Should().BeTrue();
            result.Kind.Should().Be(AnalysisDocumentKind.DeadlockXml);
            result.Document.Should().NotBeNull();
        }

        [Fact]
        public async Task OpenAsync_WhenFileIsShowPlanXml_ReturnsExecutionPlanKind()
        {
            string path = WriteFile(
                "plan.sqlplan",
                """
                <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
                  <BatchSequence />
                </ShowPlanXML>
                """);

            DocumentOpenResult result = await _service.OpenAsync(path);

            result.IsSuccess.Should().BeTrue();
            result.Kind.Should().Be(AnalysisDocumentKind.ExecutionPlanXml);
            result.Document.Should().NotBeNull();
        }

        [Fact]
        public async Task OpenAsync_WhenFileIsUnknownXml_ReturnsUnknownKind()
        {
            string path = WriteFile("unknown.xml", "<root />");

            DocumentOpenResult result = await _service.OpenAsync(path);

            result.IsSuccess.Should().BeTrue();
            result.Kind.Should().Be(AnalysisDocumentKind.Unknown);
            result.Document.Should().NotBeNull();
        }

        [Fact]
        public async Task OpenAsync_WhenPathDoesNotExist_ReturnsFailure()
        {
            string path = Path.Combine(_tempDirectory, "missing.sqlplan");

            DocumentOpenResult result = await _service.OpenAsync(path);

            result.IsSuccess.Should().BeFalse();
            result.Kind.Should().Be(AnalysisDocumentKind.Unknown);
            result.ErrorMessage.Should().Contain("does not exist");
        }

        [Fact]
        public async Task OpenAsync_WhenPathIsXel_ReturnsTraceKindWithoutLoadingXml()
        {
            string path = WriteFile("deadlocks.xel", "not xml");

            DocumentOpenResult result = await _service.OpenAsync(path);

            result.IsSuccess.Should().BeTrue();
            result.Kind.Should().Be(AnalysisDocumentKind.XelDeadlockTrace);
            result.Document.Should().BeNull();
        }

        [Fact]
        public void ClassifyXml_WhenShowPlanNamespaceContainsShowplan_ReturnsExecutionPlanKind()
        {
            var document = XDocument.Parse(
                """
                <ShowPlanXML xmlns="urn:test:showplan">
                  <BatchSequence />
                </ShowPlanXML>
                """);

            AnalysisDocumentKind kind = _service.ClassifyXml(document);

            kind.Should().Be(AnalysisDocumentKind.ExecutionPlanXml);
        }

        [Theory]
        [InlineData("deadlocks.xel", AnalysisDocumentKind.XelDeadlockTrace)]
        [InlineData("deadlock.xdl", AnalysisDocumentKind.DeadlockXml)]
        [InlineData("deadlock.xml", AnalysisDocumentKind.DeadlockXml)]
        public void ClassifyDeadlockOpenPath_WhenDeadlockPickerPathIsKnown_ReturnsExpectedKind(
            string fileName,
            AnalysisDocumentKind expected)
        {
            AnalysisDocumentKind kind = _service.ClassifyDeadlockOpenPath(fileName);

            kind.Should().Be(expected);
        }

        [Theory]
        [InlineData("deadlock.xml", AnalysisDocumentKind.DeadlockXml)]
        [InlineData("deadlock.xdl", AnalysisDocumentKind.DeadlockXml)]
        [InlineData("trace.xel", AnalysisDocumentKind.XelDeadlockTrace)]
        [InlineData("plan.sqlplan", AnalysisDocumentKind.ExecutionPlanXml)]
        [InlineData("notes.txt", AnalysisDocumentKind.Unknown)]
        public void ClassifyDroppedPath_WhenPathIsDropped_ReturnsExpectedKind(
            string fileName,
            AnalysisDocumentKind expected)
        {
            AnalysisDocumentKind kind = _service.ClassifyDroppedPath(fileName);

            kind.Should().Be(expected);
        }

        private string WriteFile(string fileName, string contents)
        {
            string path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
