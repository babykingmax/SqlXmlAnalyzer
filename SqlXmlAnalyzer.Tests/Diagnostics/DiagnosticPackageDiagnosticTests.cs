using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class DiagnosticPackageDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP27-diagnostics-" + Guid.NewGuid().ToString("N"));
    public DiagnosticPackageDiagnosticTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownSaveFailure_CreatesRealDumpOnceOrReportsDumpFailure(bool dumpFails)
    {
        var dumpWriter = new DumpWriter(dumpFails); var reporter = new UnexpectedErrorReporter(dumpWriter, _directory);
        var error = new InvalidOperationException("synthetic IMP27 export failure");
        var exporter = new DiagnosticPackageExporter(new DiagnosticPackageTests.ActionWriter((_, _, _) => throw error), reporter);
        var package = DiagnosticPackageTests.Prepare();
        string path = Path.Combine(_directory, "output.zip");
        exporter.Save(package, path).Success.Should().BeFalse();
        exporter.Save(package, path).Message.Should().Contain("DUMP");
        dumpWriter.Calls.Should().Be(1); File.Exists(path).Should().BeFalse();
        var diagnostic = reporter.Report(error, "verify");
        diagnostic.DumpCreated.Should().Be(!dumpFails);
        File.Exists(diagnostic.MetadataPath).Should().BeTrue();
        if (dumpFails) diagnostic.Summary.Should().Contain("DUMP 生成失败");
        else
        {
            MinidumpValidator.Validate(diagnostic.DumpPath!);
            var withDump = DiagnosticPackageTests.Prepare(options: new(DumpPath: diagnostic.DumpPath));
            var entry = withDump.Entries.Single(e => e.Name == "process.dmp");
            entry.IsBinary.Should().BeTrue(); entry.Privacy.Should().Be("RawSensitive");
            entry.Preview.Should().Contain("不能据此认定可公开分享");
        }
        package.Entries.Should().NotContain(e => e.Name == "process.dmp");
    }

    [Fact]
    public void PackageOperations_RespectBuildLogLevelsIncludingForcedVerbose()
    {
        string log = Path.Combine(_directory, "levels.log");
        string selected = Path.Combine(_directory, "selected.log"); File.WriteAllText(selected, "SENSITIVE-MARKER");
        Logger.Shutdown(); Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: log);
        try
        {
            DiagnosticPackageTests.Prepare(options: new(LogPath: selected));
            var reporter = new UnexpectedErrorReporter(new DumpWriter(true), _directory);
            var package = DiagnosticPackageTests.Prepare();
            new DiagnosticPackageExporter(unexpectedErrors: reporter).Save(package, Path.Combine(_directory, "wrong.sql"));
            new DiagnosticPackageExporter(new DiagnosticPackageTests.ActionWriter((_, _, _) => throw new InvalidOperationException("synthetic package error")), reporter)
                .Save(package, Path.Combine(_directory, "unknown.zip"));
        }
        finally { Logger.Shutdown(); }
        string text = File.ReadAllText(log);
        text.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("SENSITIVE-MARKER");
#if DEBUG
        text.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        text.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]").And.NotContain("[VERBOSE]");
#endif
    }

    [Fact]
    public void BrokenDiagnosticReporter_DoesNotHidePrimaryErrorOrPublishArchive()
    {
        var exporter = new DiagnosticPackageExporter(new DiagnosticPackageTests.ActionWriter((_, _, _) => throw new InvalidOperationException("primary failure")), new BrokenReporter());
        var result = exporter.Save(DiagnosticPackageTests.Prepare(), Path.Combine(_directory, "broken.zip"));
        result.Success.Should().BeFalse(); result.Message.Should().Contain("primary failure").And.Contain("诊断组件失败");
        File.Exists(Path.Combine(_directory, "broken.zip")).Should().BeFalse();
    }
    private sealed class BrokenReporter : IUnexpectedErrorReporter
    { public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("synthetic reporter failure"); }
    private sealed class DumpWriter(bool fails) : ICrashDumpWriter
    {
        public int Calls;
        public void Write(string path) { Calls++; if (fails) throw new IOException("synthetic dump failure"); new WindowsMiniDumpWriter().Write(path); }
    }
    public void Dispose() { Logger.Shutdown(); Directory.Delete(_directory, true); }
}
