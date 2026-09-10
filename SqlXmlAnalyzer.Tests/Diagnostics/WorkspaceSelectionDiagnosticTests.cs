using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class WorkspaceSelectionDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP20-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UnexpectedFailure_ProducesValidatedDumpOrExplicitFailure_AndClearsSelection(bool deadlock, bool dumpFails)
    {
        var writer = new Writer(dumpFails); var reporter = new UnexpectedErrorReporter(writer, _directory);
        var error = new InvalidOperationException("IMP20 synthetic unknown failure");
        string status;
        if (deadlock)
        {
            var vm = new DeadlockWorkspaceViewModel(reporter);
            vm.ReportFailure(error); vm.ReportFailure(error); status = vm.Status;
            vm.Analysis.Should().BeNull(); vm.SourceTarget.Should().BeNull();
        }
        else
        {
            var vm = new PlanWorkspaceViewModel(reporter, _ => throw error);
            vm.Open(WorkspaceSelectionTests.Input().Document!, null, []);
            vm.Open(WorkspaceSelectionTests.Input().Document!, null, []); status = vm.Status;
            vm.Selection.Should().BeNull(); vm.SourceTarget.Should().BeNull();
        }
        var incident = reporter.Report(error, "deduplication-check");
        writer.Calls.Should().Be(1); incident.DumpCreated.Should().Be(!dumpFails);
        status.Should().Contain("DUMP");
        if (!dumpFails) MinidumpValidator.Validate(incident.DumpPath!);
        else status.Should().Contain("DUMP 生成失败");
        File.ReadAllText(incident.MetadataPath!).Should().Contain(deadlock ? "IMP20.DeadlockWorkspace.Selection" : "IMP20.PlanWorkspace.Selection");
    }

    [Fact]
    public void DiagnosticProviderFailure_PreservesUsefulErrorState()
    {
        var vm = new PlanWorkspaceViewModel(new FailingReporter(), _ => throw new InvalidOperationException("primary failure"));
        vm.Open(WorkspaceSelectionTests.Input().Document!, null, []);
        vm.Status.Should().Contain("primary failure").And.Contain("诊断组件失败");
    }

    [Fact]
    public void ExpectedCancellationOrInputFailure_DoesNotRequestDump()
    {
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        foreach (Exception exception in new Exception[] { new OperationCanceledException(), new IOException("expected input failure") })
        {
            var plan = new PlanWorkspaceViewModel(reporter, _ => throw exception);
            plan.Open(WorkspaceSelectionTests.Input().Document!, null, []);
            var deadlock = new DeadlockWorkspaceViewModel(reporter); deadlock.ReportFailure(exception);
            plan.Selection.Should().BeNull(); deadlock.Analysis.Should().BeNull();
        }
        reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void Logs_FollowBuildMode_WithoutSourceSqlOrObjectNames()
    {
        Directory.CreateDirectory(_directory); string path = Path.Combine(_directory, "selection.log");
        var stdout = new StringWriter(); var stderr = new StringWriter(); var originalOut = Console.Out; var originalError = Console.Error;
        Logger.Shutdown();
        try
        {
            Console.SetOut(stdout); Console.SetError(stderr); Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var input = WorkspaceSelectionTests.Input();
            var vm = new PlanWorkspaceViewModel(); vm.Open(input.Document!, null, []);
            vm.SelectedStatement = vm.Statements[1];
            vm.ReportFailure(new IOException("synthetic expected failure"));
            var fault = new PlanWorkspaceViewModel(new UnexpectedErrorReporter(new Writer(true), _directory),
                _ => throw new InvalidOperationException("synthetic unknown failure"));
            fault.Open(input.Document!, null, []);
        }
        finally { Logger.Shutdown(); Console.SetOut(originalOut); Console.SetError(originalError); }
        stdout.ToString().Should().BeEmpty();
        foreach (var output in new[] { File.ReadAllText(path), stderr.ToString() })
        {
            output.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("SELECT V").And.NotContain("[demo]").And.NotContain("[IX_U]");
#if DEBUG
            output.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            output.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    private sealed class Writer(bool fails) : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path) { Calls++; if (fails) throw new IOException("synthetic dump failure"); new WindowsMiniDumpWriter().Write(path); }
    }
    private sealed class FailingReporter : IUnexpectedErrorReporter
    {
        public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("synthetic reporter failure");
    }
    public void Dispose()
    {
        Logger.Shutdown();
        string root = Path.GetFullPath(Path.GetTempPath());
        if (!Path.GetFullPath(_directory).StartsWith(Path.Combine(root, "SqlXmlAnalyzer-IMP20-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup target");
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
