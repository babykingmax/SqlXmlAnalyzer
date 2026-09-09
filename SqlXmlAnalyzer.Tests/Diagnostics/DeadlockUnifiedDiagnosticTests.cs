using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class DeadlockUnifiedDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-IMP13-{Guid.NewGuid():N}");

    [Fact]
    public void UnexpectedGraphFailure_WritesNativeDumpOnceAndPreservesOriginalException()
    {
        var error = new InvalidOperationException("IMP-13 synthetic graph failure");
        var writer = new CountingWriter();
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        var service = new DeadlockAnalysisService(reporter, graphBuilder: (_, _, _) => throw error);
        Action analyze = () => service.Analyze(DeadlockUnifiedGraphTests.Overlap());
        analyze.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        var report = reporter.Report(error, "deduplication");
        report.DumpCreated.Should().BeTrue(report.Failure);
        MinidumpValidator.Validate(report.DumpPath!);
        File.ReadAllText(report.MetadataPath!).Should().Contain("DeadlockAnalysisService.Analyze").And.Contain("synthetic graph failure");
        writer.Calls.Should().Be(1);
    }

    [Fact]
    public void DumpWriterFailure_PreservesOriginalFailureAndReportsCaptureFailure()
    {
        var error = new InvalidOperationException("IMP-13 original failure");
        var reporter = new UnexpectedErrorReporter(new FailingWriter(), _directory);
        Action analyze = () => new DeadlockAnalysisService(reporter, graphBuilder: (_, _, _) => throw error).Analyze(DeadlockUnifiedGraphTests.Overlap());
        analyze.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        var report = reporter.Report(error, "deduplication");
        report.DumpCreated.Should().BeFalse();
        report.Summary.Should().Contain("DUMP 生成失败");
        File.ReadAllText(report.MetadataPath!).Should().Contain("IMP-13 original failure");
    }

    [Fact]
    public void ThrowingReporter_DoesNotReplaceTheOriginalFailure()
    {
        var error = new InvalidOperationException("synthetic failure");
        Action analyze = () => new DeadlockAnalysisService(new ThrowingReporter(), graphBuilder: (_, _, _) => throw error)
            .Analyze(DeadlockUnifiedGraphTests.Overlap());
        analyze.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
    }

    [Theory]
    [InlineData("links")]
    [InlineData("pairs")]
    [InlineData("invalid")]
    [InlineData("cancel")]
    [InlineData("cancel-after-parse")]
    public void ExpectedFailuresAndCancellation_DoNotRequestDump(string kind)
    {
        var reporter = new RecordingReporter();
        var options = kind switch {
            "links" => new DeadlockGraphOptions { MaxResourceLinks = 1 },
            "pairs" => new DeadlockGraphOptions { MaxWaitForEdges = 1 },
            _ => new DeadlockGraphOptions()
        };
        var document = DeadlockUnifiedGraphTests.Overlap();
        if (kind == "invalid") document.Root!.Element("process-list")!.Remove();
        using var cancellation = new CancellationTokenSource();
        if (kind == "cancel") cancellation.Cancel();
        var service = kind == "cancel-after-parse"
            ? new DeadlockAnalysisService(reporter, graphBuilder: (parsed, limits, token) => {
                cancellation.Cancel(); return DeadlockGraphBuilder.Build(parsed, limits, token, reporter); })
            : new DeadlockAnalysisService(reporter, options);
        Action analyze = () => service.Analyze(document, cancellation.Token);
        if (kind.StartsWith("cancel")) analyze.Should().Throw<OperationCanceledException>();
        else if (kind == "invalid") analyze.Should().Throw<InvalidDataException>();
        else analyze.Should().Throw<SqlXmlAnalyzer.Core.Services.DocumentBudgetExceededException>();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void CandidatePairBudget_AppliesEvenToSelfReferences()
    {
        var document = DeadlockUnifiedGraphTests.Document(("a", "a", "self"));
        document.Descendants("owner-list").Single().Add(Enumerable.Range(0, 20).Select(_ => new System.Xml.Linq.XElement("owner", new System.Xml.Linq.XAttribute("id", "a"))));
        var reporter = new RecordingReporter();
        Action analyze = () => new DeadlockAnalysisService(reporter, new() { MaxWaitForEdges = 10 }).Analyze(document);
        analyze.Should().Throw<SqlXmlAnalyzer.Core.Services.DocumentBudgetExceededException>();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void TimelineEntry_UsesSameBudgetsAndCancellationPolicy()
    {
        var reporter = new RecordingReporter();
        var parser = new DeadlockTimelineParser();
        var result = parser.ParseResult(DeadlockUnifiedGraphTests.Overlap().ToString(), options: new() { MaxWaitForEdges = 1 }, unexpectedErrors: reporter);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        Action cancel = () => parser.ParseResult("invalid", new CancellationToken(true), unexpectedErrors: reporter);
        cancel.Should().Throw<OperationCanceledException>();
        parser.ParseResult("<broken>", unexpectedErrors: reporter).IsSuccess.Should().BeFalse();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void BuildModes_EnforceRequiredLogLevelsWithoutLoggingSourceValues()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "deadlock.log");
        Logger.Shutdown();
        try
        {
            Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var document = DeadlockUnifiedGraphTests.Overlap();
            document.Descendants("inputbuf").First().Value = "secret-sql-marker";
            document.Descendants("owner").First().SetAttributeValue("id", "secret-id-marker");
            new DeadlockAnalysisService(options: new() { MaxCycleLength = 1 }).Analyze(document);
            Logger.Error("imp13-error-marker");
            Logger.Critical("imp13-fatal-marker");
        }
        finally { Logger.Shutdown(); }
        string log = File.ReadAllText(path);
        log.Should().Contain("[ERROR]").And.Contain("imp13-error-marker").And.Contain("[CRITICAL]").And.Contain("imp13-fatal-marker")
            .And.NotContain("secret-sql-marker").And.NotContain("secret-id-marker");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]").And.Contain("IMP-13");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]");
#endif
    }

    private sealed class CountingWriter : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path) { Calls++; new WindowsMiniDumpWriter().Write(path); }
    }
    private sealed class FailingWriter : ICrashDumpWriter
    {
        public void Write(string path) => throw new IOException("synthetic disk-full");
    }
    private sealed class RecordingReporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; return new(null, null, "test"); }
    }
    private sealed class ThrowingReporter : IUnexpectedErrorReporter
    {
        public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("synthetic reporter failure");
    }
    public void Dispose()
    {
        Logger.Shutdown();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
