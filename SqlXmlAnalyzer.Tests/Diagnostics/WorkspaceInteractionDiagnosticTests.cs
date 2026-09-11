using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class WorkspaceInteractionDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP25-" + Guid.NewGuid().ToString("N"));
    public WorkspaceInteractionDiagnosticTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownUiFailure_ProducesDumpOrReportsCaptureFailure(bool writerFails)
    {
        var writer = new Writer(writerFails); var reporter = new UnexpectedErrorReporter(writer, _directory);
        var service = new WorkspaceInteractionService(reporter); string status = "";
        var exception = new InvalidOperationException("synthetic interaction failure");
        service.Run("Keyboard", () => true, () => throw exception, value => status = value).Should().BeFalse();
        service.Run("Keyboard", () => true, () => throw exception, value => status = value).Should().BeFalse();
        writer.Calls.Should().Be(1); status.Should().Contain("DUMP");
        var report = reporter.Report(exception, "deduplicate");
        if (writerFails) status.Should().Contain("失败");
        else { report.DumpCreated.Should().BeTrue(); MinidumpValidator.Validate(report.DumpPath!); }
    }

    [Fact]
    public void Commands_LogBuildAllowedLevelsAndExpectedErrorsDoNotDump()
    {
        string path = Path.Combine(_directory, "interaction.log"); var writer = new Writer(true);
        var service = new WorkspaceInteractionService(new UnexpectedErrorReporter(writer, _directory));
        Logger.Shutdown(); Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
        try
        {
            service.Run("Enabled", () => true, () => { }, _ => { });
            service.Run("Cancelled", () => true, () => throw new OperationCanceledException(), _ => { });
            service.Run("Layout", () => true, () => throw new InvalidDataException("invalid layout"), _ => { });
            writer.Calls.Should().Be(0);
            service.Run("Theme", () => true, () => throw new InvalidOperationException("synthetic theme failure"), _ => { });
        }
        finally { Logger.Shutdown(); }
        string log = File.ReadAllText(path);
        log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
    }

    private sealed class Writer(bool fails) : ICrashDumpWriter
    {
        public int Calls;
        public void Write(string path) { Calls++; if (fails) throw new IOException("synthetic dump failure"); new WindowsMiniDumpWriter().Write(path); }
    }
    public void Dispose() { Logger.Shutdown(); Directory.Delete(_directory, true); }
}
