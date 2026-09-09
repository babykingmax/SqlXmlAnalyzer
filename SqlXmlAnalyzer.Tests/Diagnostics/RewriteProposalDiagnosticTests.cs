using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Refactoring;
using SqlXmlAnalyzer.Tests.Refactoring;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class RewriteProposalDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP18-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Review_UnknownFailure_RecordsRealDumpOrFailureAndPreservesOriginalException(bool dumpFails)
    {
        var writer = new Writer(dumpFails);
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        var error = new InvalidOperationException("synthetic-imp18-fault");
        var (first, _) = RewriteProposalTests.Chain();
        RewriteReview? review = null;
        Action act = () => review = new RewriteProposalService(reporter).Review("SELECT 1;", Fault(first, error), [first.Id]);
        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        review.Should().BeNull();
        var incident = reporter.Report(error, "verify-deduplication");
        writer.Calls.Should().Be(1);
        incident.DumpCreated.Should().Be(!dumpFails);
        if (!dumpFails) MinidumpValidator.Validate(incident.DumpPath!);
        else incident.Summary.Should().Contain("DUMP 生成失败");
        File.ReadAllText(incident.MetadataPath!).Should().Contain("RewriteProposal.Review");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Review_ExpectedIoOrCancellation_DoesNotCaptureDump(bool cancel)
    {
        var reporter = new RecordingReporter();
        var (first, _) = RewriteProposalTests.Chain();
        Exception error = cancel ? new OperationCanceledException() : new IOException("storage unavailable");
        Action act = () => new RewriteProposalService(reporter).Review("SELECT 1;", Fault(first, error));
        act.Should().Throw<Exception>().Which.Should().BeSameAs(error);
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void Review_DiagnosticProviderFailure_DoesNotReplacePrimaryError()
    {
        var reporter = new RecordingReporter { Throw = true };
        var (first, _) = RewriteProposalTests.Chain();
        var error = new InvalidOperationException("synthetic primary failure");
        Action act = () => new RewriteProposalService(reporter).Review("SELECT 1;", Fault(first, error));
        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        reporter.Calls.Should().Be(1);
    }

    [Fact]
    public void Logging_UsesRequiredBuildLevelsWithoutSqlInFileOrStderr()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "rewrite.log");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;
        Logger.Shutdown();
        try
        {
            Console.SetOut(stdout); Console.SetError(stderr);
            Logger.Initialize(customLogFilePath: path);
            var result = RewriteProposalTests.Run();
            var reporter = new UnexpectedErrorReporter(new Writer(true), _directory);
            var service = new RewriteProposalService(reporter);
            Action invalid = () => service.Review(RewriteProposalTests.Sql, result.Review!.Proposals, ["unknown"]);
            invalid.Should().Throw<InvalidDataException>();
            Action unknown = () => service.Review(RewriteProposalTests.Sql,
                Fault(result.Review!.Proposals[0], new InvalidOperationException("synthetic log fault")));
            unknown.Should().Throw<InvalidOperationException>();
        }
        finally { Logger.Shutdown(); Console.SetOut(previousOut); Console.SetError(previousError); }
        stdout.ToString().Should().BeEmpty();
        foreach (string output in new[] { File.ReadAllText(path), stderr.ToString() })
        {
            output.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("PrivateUsers").And.NotContain("N'admin'");
#if DEBUG
            output.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            output.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    private static IEnumerable<RewriteProposal> Fault(RewriteProposal first, Exception error)
    {
        yield return first;
        throw error;
    }
    private sealed class Writer(bool fail) : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path)
        {
            Calls++;
            if (fail) throw new IOException("synthetic dump failure");
            new WindowsMiniDumpWriter().Write(path);
        }
    }
    private sealed class RecordingReporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public bool Throw { get; init; }
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Calls++;
            if (Throw) throw new IOException("synthetic reporter failure");
            return new(null, null, "synthetic");
        }
    }
    public void Dispose()
    {
        Logger.Shutdown();
        string full = Path.GetFullPath(_directory);
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP18-");
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test cleanup path");
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }
}
