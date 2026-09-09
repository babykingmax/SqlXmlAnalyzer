using System.IO;
using System.Text;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Privacy;

namespace SqlXmlAnalyzer.Tests.Privacy;

public sealed class RedactedPlanExportServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-Imp05Tests-" + Guid.NewGuid().ToString("N"));
    public RedactedPlanExportServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ExportFile_UsesNewFileAndPreservesOriginalBytes()
    {
        string input = Path.Combine(_directory, "input.sqlplan"), output = Path.Combine(_directory, "redacted.sqlplan");
        File.WriteAllText(input, PlanRedactionServiceTests.Fixture().ToString(), Encoding.Unicode);
        byte[] before = File.ReadAllBytes(input);
        var result = new RedactedPlanExportService().ExportFile(input, output);
        result.IsSuccess.Should().BeTrue(result.Message);
        File.ReadAllText(output).Should().NotContain(PlanRedactionServiceTests.Marker);
        File.ReadAllBytes(input).Should().Equal(before);
        SafeXmlHelper.LoadSafe(output).Root.Should().NotBeNull();
        Directory.GetFiles(_directory, "*.partial").Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportFile_ExistingSourceOrTarget_IsNeverOverwritten(bool sameFile)
    {
        string input = Path.Combine(_directory, "input.sqlplan"), output = sameFile ? input : Path.Combine(_directory, "existing.sqlplan");
        File.WriteAllText(input, PlanRedactionServiceTests.Fixture().ToString());
        if (!sameFile) File.WriteAllText(output, "existing data");
        byte[] before = File.ReadAllBytes(output);
        new RedactedPlanExportService().ExportFile(input, output).IsSuccess.Should().BeFalse();
        File.ReadAllBytes(output).Should().Equal(before);
        Directory.GetFiles(_directory, "*.partial").Should().BeEmpty();
    }

    [Fact]
    public void Export_BlockedDocument_NeverInvokesWriter()
    {
        var input = PlanRedactionServiceTests.Fixture(); input.Root!.SetAttributeValue("Unrecognized", "secret");
        var writer = new ThrowingWriter();
        var result = new RedactedPlanExportService(writer).Export(new PlanRedactionService().Redact(input), "not-used.sqlplan");
        result.IsSuccess.Should().BeFalse(); writer.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Export_WriterFault_ReportsFailureAndDumpsOnlyUnexpectedError(bool unknown)
    {
        var diagnostics = new PlanRedactionServiceTests.RecordingDiagnostics();
        var writer = new ThrowingWriter { Unknown = unknown };
        var result = new RedactedPlanExportService(writer, diagnostics).Export(new PlanRedactionService().Redact(PlanRedactionServiceTests.Fixture()), "not-used.sqlplan");
        result.IsSuccess.Should().BeFalse(); writer.Calls.Should().Be(1);
        diagnostics.Calls.Should().Be(unknown ? 1 : 0);
        result.Message.Should().NotContain(PlanRedactionServiceTests.Marker);
    }

    [Fact]
    public void ExportFile_DtdOrMalformedInput_ProducesNoOutputAndNoSensitiveErrorText()
    {
        string input = Path.Combine(_directory, "input.sqlplan"), output = Path.Combine(_directory, "redacted.sqlplan");
        File.WriteAllText(input, "<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///SECRET_IMP05_9A51'>]><x>&secret;</x>");
        var result = new RedactedPlanExportService().ExportFile(input, output);
        result.IsSuccess.Should().BeFalse();
        result.Message.Should().NotContain(PlanRedactionServiceTests.Marker);
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public void Export_CancellationBeforeWrite_LeavesNoArtifacts()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var result = new RedactedPlanExportService().Export(new PlanRedactionService().Redact(PlanRedactionServiceTests.Fixture()), Path.Combine(_directory, "redacted.sqlplan"), cts.Token);
        result.IsSuccess.Should().BeFalse();
        Directory.GetFiles(_directory).Should().BeEmpty();
    }

    private sealed class ThrowingWriter : IRedactedPlanWriter
    {
        public int Calls { get; private set; }
        public bool Unknown { get; init; }
        public void WriteNew(string path, byte[] bytes, CancellationToken token)
        {
            Calls++; throw Unknown ? new InvalidOperationException("synthetic writer fault") : new IOException(PlanRedactionServiceTests.Marker);
        }
    }

    [Fact]
    public void Export_DumpReporterFails_PreservesTheOriginalFailureAndExplainsMissingDump()
    {
        var result = new RedactedPlanExportService(new ThrowingWriter { Unknown = true }, new BrokenDiagnostics())
            .Export(new PlanRedactionService().Redact(PlanRedactionServiceTests.Fixture()), "unused.sqlplan");
        result.IsSuccess.Should().BeFalse();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.DumpCreated.Should().BeFalse();
        result.Message.Should().Contain("DUMP 生成失败");
    }

    private sealed class BrokenDiagnostics : SqlXmlAnalyzer.Core.Diagnostics.IUnexpectedErrorReporter
    {
        public SqlXmlAnalyzer.Core.Diagnostics.UnexpectedErrorReport Report(Exception exception, string operation) =>
            throw new IOException("synthetic diagnostic storage failure");
    }

    public void Dispose()
    {
        string path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp05Tests-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup target");
        Directory.Delete(path, true);
    }
}
