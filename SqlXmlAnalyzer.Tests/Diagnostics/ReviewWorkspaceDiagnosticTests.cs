using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Refactoring;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class ReviewWorkspaceDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP21-Diagnostics-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnknownClipboardFailure_ShowsValidatedDumpOrExplicitFailure(bool index, bool dumpFails)
    {
        var writer = new Writer(dumpFails); var reporter = new UnexpectedErrorReporter(writer, _directory);
        var error = new InvalidOperationException("synthetic IMP21 unknown output failure");
        string status;
        if (index)
        {
            var vm = new IndexSandboxViewModel(ReviewWorkspaceTests.Suggestion(), unexpectedErrors: reporter);
            vm.CopyScript(_ => throw error); vm.CopyScript(_ => throw error); status = vm.OutputStatus;
            vm.CanExport.Should().BeTrue("a clipboard error does not invalidate verified DDL");
        }
        else
        {
            var vm = new RewriteReviewViewModel(RewriteProposalTests.Sql, RewriteProposalTests.Run().Review!, reporter);
            vm.Items[0].IsSelected = true;
            vm.CopyCandidate(_ => throw error); vm.CopyCandidate(_ => throw error); status = vm.Error;
            vm.CanApply.Should().BeFalse(); vm.ApplyStatusText.Should().Be("尚未应用");
        }
        var incident = reporter.Report(error, "deduplication-check");
        writer.Calls.Should().Be(1); status.Should().Contain("DUMP");
        incident.DumpCreated.Should().Be(!dumpFails);
        if (dumpFails) status.Should().Contain("DUMP 生成失败");
        else MinidumpValidator.Validate(incident.DumpPath!);
        File.ReadAllText(incident.MetadataPath!).Should().Contain("IMP21.ReviewOutput.Copy");
    }

    [Fact]
    public void UnknownIndexRefreshFailure_ClearsBothScriptsAndReportsDump()
    {
        var reporter = new UnexpectedErrorReporter(new Writer(false), _directory);
        var vm = new IndexSandboxViewModel(ReviewWorkspaceTests.Suggestion(), unexpectedErrors: reporter);
        var error = new InvalidOperationException("synthetic IMP21 binding failure");
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CurrentScore)) throw error; };
        var notifications = new HashSet<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        vm.MaxDop = "2";
        vm.CreateIndexStatement.Should().BeEmpty(); vm.RollbackStatement.Should().BeEmpty(); vm.CanExport.Should().BeFalse();
        vm.Error.Should().Contain("DUMP");
        notifications.Should().Contain(nameof(vm.RollbackStatement)).And.Contain(nameof(vm.CompiledIndexName))
            .And.Contain(nameof(vm.CanExport)).And.Contain(nameof(vm.Error)).And.Contain(nameof(vm.OutputStatus));
        MinidumpValidator.Validate(reporter.Report(error, "duplicate").DumpPath!);
    }

    [Fact]
    public void BrokenDiagnosticProvider_PreservesPrimaryFailureAndDiagnosticStatus()
    {
        var result = new ReviewOutputService(new BrokenReporter()).Copy("SELECT 1;", _ => throw new InvalidOperationException("primary output failure"));
        result.Succeeded.Should().BeFalse(); result.Message.Should().Contain("primary output failure").And.Contain("诊断组件失败");
    }

    [Fact]
    public void ReviewLogs_RespectBuildModeAndOmitRawSqlAndObjectIdentity()
    {
        Directory.CreateDirectory(_directory); string path = Path.Combine(_directory, "review.log");
        var stdout = new StringWriter(); var stderr = new StringWriter(); var previousOut = Console.Out; var previousError = Console.Error;
        Logger.Shutdown();
        try
        {
            Console.SetOut(stdout); Console.SetError(stderr); Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var service = new ReviewOutputService(new UnexpectedErrorReporter(new Writer(true), _directory));
            service.Copy("SELECT PrivateColumn FROM PrivateTable;", _ => { });
            service.Copy("", _ => { });
            service.Copy("SELECT PrivateColumn FROM PrivateTable;", _ => throw new InvalidOperationException("synthetic IMP21 failure"));
            var index = new IndexSandboxViewModel(ReviewWorkspaceTests.Suggestion());
            foreach (var column in index.KeyColumns.ToArray()) index.RemoveKeyColumnCommand.Execute(column);
        }
        finally { Logger.Shutdown(); Console.SetOut(previousOut); Console.SetError(previousError); }
        stdout.ToString().Should().BeEmpty();
        foreach (string output in new[] { File.ReadAllText(path), stderr.ToString() })
        {
            output.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("PrivateColumn").And.NotContain("PrivateTable").And.NotContain("[server]").And.NotContain("[Orders]");
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
    private sealed class BrokenReporter : IUnexpectedErrorReporter
    {
        public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("synthetic reporter failure");
    }
    public void Dispose()
    {
        Logger.Shutdown();
        if (!Path.GetFullPath(_directory).StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP21-Diagnostics-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
