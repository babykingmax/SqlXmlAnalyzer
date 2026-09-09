using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Refactoring;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class SqlSemanticDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP19Diagnostic-" + Guid.NewGuid().ToString("N"));

    private async Task<PrepareSqlRewriteResult> Fail(Exception exception, IUnexpectedErrorReporter reporter)
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "source.sql");
        const string sql = "SELECT N'SyntheticPrivateIMP19' AS Value;";
        File.WriteAllText(path, sql);
        var proposals = new RewriteProposalService(reporter);
        var proposal = proposals.Propose(sql, sql, "SELECT N'candidate' AS Value;", "TEST", "1", "test", [], new(sql));
        var service = new ReviewedSqlApplyService(new SqlWritebackService(new PhysicalSqlWritebackFileSystem()), new FaultRunner(exception), reporter);
        var result = await service.PrepareAsync(path, sql, proposals.Review(sql, [proposal], [proposal.Id]), new([new("test", "")]));
        result.CanApply.Should().BeFalse();
        File.ReadAllText(path).Should().Be(sql);
        Directory.GetFiles(_directory, "*.bak").Should().BeEmpty();
        return result;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepare_UnknownErrorCreatesDumpOrReportsDumpFailure(bool fail)
    {
        var writer = new Writer(fail);
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        var exception = new InvalidOperationException("synthetic IMP19 failure");
        var result = await Fail(exception, reporter);
        writer.Calls.Should().Be(1);
        result.Diagnostic.Should().NotBeNull();
        if (fail) result.Diagnostic!.Summary.Should().Contain("DUMP 生成失败");
        else MinidumpValidator.Validate(result.Diagnostic!.DumpPath!);
        File.ReadAllText(result.Diagnostic!.MetadataPath!).Should().Contain("ReviewedApply.Prepare");
        reporter.Report(exception, "deduplicate");
        writer.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Prepare_ExpectedFailureDoesNotDumpAndDiagnosticFailureKeepsError()
    {
        var reporter = new BrokenReporter();
        (await Fail(new IOException("expected read error"), reporter)).Diagnostic.Should().BeNull();
        reporter.Calls.Should().Be(0);
        var unknown = await Fail(new InvalidOperationException("synthetic primary"), reporter);
        reporter.Calls.Should().Be(1);
        unknown.Diagnostic!.Summary.Should().Contain("诊断组件失败");
    }

    [Fact]
    public async Task Logging_FollowsBuildLevelsAndDoesNotLogSql()
    {
        Directory.CreateDirectory(_directory);
        string log = Path.Combine(_directory, "semantic.log");
        var stdout = new StringWriter(); var stderr = new StringWriter();
        var originalOut = Console.Out; var originalError = Console.Error;
        Logger.Shutdown();
        try
        {
            Console.SetOut(stdout); Console.SetError(stderr); Logger.Initialize(customLogFilePath: log);
            await Fail(new OperationCanceledException(), new BrokenReporter());
            await Fail(new IOException("synthetic expected"), new BrokenReporter());
            await Fail(new InvalidOperationException("synthetic fatal"), new UnexpectedErrorReporter(new Writer(true), _directory));
        }
        finally { Logger.Shutdown(); Console.SetOut(originalOut); Console.SetError(originalError); }
        stdout.ToString().Should().BeEmpty();
        foreach (string text in new[] { File.ReadAllText(log), stderr.ToString() })
        {
            text.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("SyntheticPrivateIMP19");
#if DEBUG
            text.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            text.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    private sealed class FaultRunner(Exception exception) : ISqlSemanticRunner
    {
        public Task<SqlSemanticReport> ValidateAsync(string original, string candidate, SqlSemanticSuite suite, CancellationToken token = default) =>
            Task.FromException<SqlSemanticReport>(exception);
    }
    private sealed class Writer(bool fail) : ICrashDumpWriter
    {
        public int Calls;
        public void Write(string path)
        {
            Calls++;
            if (fail) throw new IOException("synthetic dump unavailable");
            new WindowsMiniDumpWriter().Write(path);
        }
    }
    private sealed class BrokenReporter : IUnexpectedErrorReporter
    {
        public int Calls;
        public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; throw new IOException("synthetic reporter unavailable"); }
    }
    public void Dispose()
    {
        Logger.Shutdown();
        string full = Path.GetFullPath(_directory);
        if (!full.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP19Diagnostic-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
