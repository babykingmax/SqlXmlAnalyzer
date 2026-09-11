using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Compatibility;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class CompatibilityDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP28-diagnostics-" + Guid.NewGuid().ToString("N"));
    public CompatibilityDiagnosticTests() => Directory.CreateDirectory(_directory);
    private string Legacy()
    {
        string source = Path.Combine(_directory, "legacy.pesession");
        File.WriteAllText(source, CompatibilityFixtures.Read("session-v2.0.pesession"));
        return source;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownSessionWriteFailure_CapturesRealDumpOrReportsCaptureFailure(bool dumpFails)
    {
        string source = Legacy(); byte[] original = File.ReadAllBytes(source);
        var snapshot = new TuningSessionService().Load(source);
        var dumpWriter = new DumpWriter(dumpFails);
        var reporter = new UnexpectedErrorReporter(dumpWriter, Path.Combine(_directory, "dumps"));
        var error = new InvalidOperationException("synthetic IMP28 session failure");
        var writer = new SessionCompatibilityTests.ActionWriter((_, _, _) => throw error);
        var service = new TuningSessionService(writer, reporter);
        string destination = Path.Combine(_directory, "new.pesession");
        Action save = () => service.Save(destination, snapshot.Snapshots, snapshot.PlanA, snapshot.PlanB);
        save.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        save.Should().Throw<InvalidOperationException>();
        dumpWriter.Calls.Should().Be(1);
        var diagnostic = reporter.Report(error, "verify");
        diagnostic.DumpCreated.Should().Be(!dumpFails);
        File.Exists(diagnostic.MetadataPath).Should().BeTrue();
        if (dumpFails) diagnostic.Summary.Should().Contain("DUMP 生成失败");
        else MinidumpValidator.Validate(diagnostic.DumpPath!);
        File.Exists(destination).Should().BeFalse();
        File.ReadAllBytes(source).Should().Equal(original);
        Directory.GetDirectories(_directory, ".tmp.session-*").Should().BeEmpty();
    }

    [Fact]
    public void SessionCompatibilityOperations_RespectDebugReleaseLogGate()
    {
        string log = Path.Combine(_directory, "levels.log");
        Logger.Shutdown(); Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: log);
        try
        {
            string source = Legacy();
            var snapshot = new TuningSessionService().Load(source);
            Action existing = () => new TuningSessionService().Save(source, snapshot.Snapshots, snapshot.PlanA, snapshot.PlanB);
            existing.Should().Throw<IOException>();
            var reporter = new UnexpectedErrorReporter(new DumpWriter(true), Path.Combine(_directory, "dumps"));
            var service = new TuningSessionService(new SessionCompatibilityTests.ActionWriter((_, _, _) => throw new InvalidOperationException("synthetic failure")), reporter);
            Action unknown = () => service.Save(Path.Combine(_directory, "new.pesession"), snapshot.Snapshots, snapshot.PlanA, snapshot.PlanB);
            unknown.Should().Throw<InvalidOperationException>();
        }
        finally { Logger.Shutdown(); }
        string text = File.ReadAllText(log);
        text.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("SELECT 1");
#if DEBUG
        text.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        text.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]").And.NotContain("[VERBOSE]");
#endif
    }

    private sealed class DumpWriter(bool fails) : ICrashDumpWriter
    {
        public int Calls;
        public void Write(string path) { Calls++; if (fails) throw new IOException("synthetic dump failure"); new WindowsMiniDumpWriter().Write(path); }
    }
    public void Dispose() { Logger.Shutdown(); Directory.Delete(_directory, recursive: true); }
}
