using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class UnifiedReportDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP22-Diagnostics-" + Guid.NewGuid().ToString("N"));
    public UnifiedReportDiagnosticTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownWriterFailure_CreatesValidatedDumpOrExplicitFailureAndNoReport(bool dumpFails)
    {
        var writer = new DumpWriter(dumpFails); var reporter = new UnexpectedErrorReporter(writer, _directory);
        var failure = new InvalidOperationException("synthetic report renderer failure");
        var service = new DiagnosticReportExportService(new DiagnosticReportTests.Writer((_, _, path, _) => { File.WriteAllText(path, "partial"); throw failure; }), reporter);
        var report = DiagnosticReportTests.Report(); string output = Path.Combine(_directory, "report.pdf");
        var result = service.Export(report, "pdf", output);
        service.Export(report, "pdf", output).Succeeded.Should().BeFalse();
        result.Succeeded.Should().BeFalse(); result.Message.Should().Contain("synthetic report renderer failure").And.Contain("DUMP");
        File.Exists(output).Should().BeFalse(); Directory.GetFiles(_directory, ".tmp.report-*").Should().BeEmpty();
        writer.Calls.Should().Be(1);
        var incident = reporter.Report(failure, "deduplication");
        if (dumpFails) result.Message.Should().Contain("DUMP 生成失败");
        else MinidumpValidator.Validate(incident.DumpPath!);
        File.ReadAllText(incident.MetadataPath!).Should().Contain("IMP22.ReportExport");
    }

    [Fact]
    public void BrokenReporter_PreservesPrimaryFailure()
    {
        var service = new DiagnosticReportExportService(new DiagnosticReportTests.Writer((_, _, _, _) => throw new InvalidOperationException("primary rendering failure")), new BrokenReporter());
        service.Export(DiagnosticReportTests.Report(), "pdf", Path.Combine(_directory, "report.pdf")).Message.Should().Contain("primary rendering failure").And.Contain("诊断组件失败");
    }

    [Fact]
    public void ReportLogs_RespectBuildModeAndNeverLogSuccessfulRawPayload()
    {
        var report = DiagnosticReportTests.Report(); string path = Path.Combine(_directory, "report.log");
        var output = new StringWriter(); var error = new StringWriter(); var oldOutput = Console.Out; var oldError = Console.Error;
        Logger.Shutdown();
        try
        {
            Console.SetOut(output); Console.SetError(error); Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var service = new DiagnosticReportExportService();
            service.Export(report, "json", Path.Combine(_directory, "report.json"));
            service.Export(report, "html", Path.Combine(_directory, "wrong.sql"));
            service.Export(report, "json", Path.Combine(_directory, "cancel.json"), new CancellationToken(true));
            new DiagnosticReportExportService(new DiagnosticReportTests.Writer((_, _, _, _) => throw new InvalidOperationException("synthetic unknown failure")), new UnexpectedErrorReporter(new DumpWriter(true), _directory))
                .Export(report, "json", Path.Combine(_directory, "failed.json"));
        }
        finally { Logger.Shutdown(); Console.SetOut(oldOutput); Console.SetError(oldError); }
        output.ToString().Should().BeEmpty();
        foreach (string log in new[] { error.ToString(), File.ReadAllText(path) })
        {
            log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain(Privacy.PlanRedactionServiceTests.Marker);
#if DEBUG
            log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    private sealed class DumpWriter(bool fails) : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path) { Calls++; if (fails) throw new IOException("synthetic dump writer failure"); new WindowsMiniDumpWriter().Write(path); }
    }
    private sealed class BrokenReporter : IUnexpectedErrorReporter
    {
        public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("synthetic provider failure");
    }
    public void Dispose()
    {
        Logger.Shutdown();
        if (!Path.GetFullPath(_directory).StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP22-Diagnostics-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(_directory, true);
    }
}
