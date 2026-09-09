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
            string path = WriteFile("deadlock.xdl", "<deadlock><process-list><process id='p1'/></process-list><resource-list><keylock/></resource-list></deadlock>");

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
                  <BatchSequence><Batch><Statements><StmtSimple StatementType="CREATE TABLE" /></Statements></Batch></BatchSequence>
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

            result.IsSuccess.Should().BeFalse();
            result.Status.Should().Be(InputStatus.Unrecognized);
            result.ErrorCode.Should().Be("INPUT_UNRELATED_XML");
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
            result.ErrorCode.Should().Be("INPUT_READ_ERROR");
            result.ErrorMessage.Should().Contain("无法读取输入文件");
        }

        [Fact]
        public async Task OpenAsync_WhenPathIsInvalidXel_ReturnsInvalidInsteadOfRoutingSuccess()
        {
            string path = WriteFile("deadlocks.xel", "not xml");

            DocumentOpenResult result = await _service.OpenAsync(path);

            result.IsSuccess.Should().BeFalse();
            result.Status.Should().Be(InputStatus.Invalid);
            result.ErrorCode.Should().Be("INPUT_INVALID_XEL");
            result.Kind.Should().Be(AnalysisDocumentKind.XelDeadlockTrace);
            result.Document.Should().BeNull();
        }

        [Fact]
        public void ClassifyXml_WhenNamespaceOnlyContainsShowplan_RejectsUnsupportedNamespace()
        {
            var document = XDocument.Parse(
                """
                <ShowPlanXML xmlns="urn:test:showplan">
                  <BatchSequence />
                </ShowPlanXML>
                """);

            AnalysisDocumentKind kind = _service.ClassifyXml(document);

            kind.Should().Be(AnalysisDocumentKind.Unknown);
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

        [Fact]
        public void BuildDropAction_WhenPathIsEmpty_ReturnsEmpty()
        {
            DocumentDropActionResult result = _service.BuildDropAction(" ");

            result.Status.Should().Be(DocumentDropActionStatus.Empty);
            result.Kind.Should().Be(AnalysisDocumentKind.Unknown);
            result.FilePath.Should().BeEmpty();
            result.UserMessage.Should().BeNull();
        }

        [Fact]
        public void BuildDropAction_WhenExtensionIsUnknown_DefersToContentRecognition()
        {
            DocumentDropActionResult result = _service.BuildDropAction("notes.txt");

            result.Status.Should().Be(DocumentDropActionStatus.Ready);
            result.Kind.Should().Be(AnalysisDocumentKind.Unknown);
            result.FilePath.Should().Be("notes.txt");
            result.UserMessage.Should().BeNull();
        }

        [Theory]
        [InlineData("deadlock.xdl", AnalysisDocumentKind.DeadlockXml)]
        [InlineData("trace.xel", AnalysisDocumentKind.XelDeadlockTrace)]
        [InlineData("plan.sqlplan", AnalysisDocumentKind.ExecutionPlanXml)]
        public void BuildDropAction_WhenPathIsSupported_ReturnsReady(
            string fileName,
            AnalysisDocumentKind expected)
        {
            DocumentDropActionResult result = _service.BuildDropAction(fileName);

            result.Status.Should().Be(DocumentDropActionStatus.Ready);
            result.Kind.Should().Be(expected);
            result.FilePath.Should().Be(fileName);
            result.UserMessage.Should().BeNull();
        }

        private string WriteFile(string fileName, string contents)
        {
            string path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
