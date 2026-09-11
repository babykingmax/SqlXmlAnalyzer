using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class PlanOperatorFactsDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-IMP11-{Guid.NewGuid():N}");

    [Fact]
    public void Read_UnknownFailureCreatesValidatedNativeDumpAndPreservesException()
    {
        var error = new InvalidOperationException("IMP-11 synthetic extraction failure");
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), _directory);
        var service = new PlanOperatorFactsService(new ThrowingReader(error), reporter);
        var op = PlanOperatorFactsServiceTests.Parse();
        Action read = () => service.Read(op, op.Name.Namespace);
        read.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        var report = reporter.Report(error, "deduplication");
        report.DumpCreated.Should().BeTrue(report.Failure);
        MinidumpValidator.Validate(report.DumpPath!);
        File.ReadAllText(report.MetadataPath!).Should().Contain("PlanOperatorFactsService.Read").And.Contain("ThrowingReader");
        Directory.GetDirectories(_directory).Should().ContainSingle();
    }

    [Fact]
    public void Read_CancellationAndInvalidInputDoNotRequestDump()
    {
        var reporter = new RecordingReporter();
        var service = new PlanOperatorFactsService(unexpectedErrors: reporter);
        var op = PlanOperatorFactsServiceTests.Parse();
        Action cancel = () => service.Read(op, op.Name.Namespace, new CancellationToken(true));
        cancel.Should().Throw<OperationCanceledException>();
        Action invalid = () => service.Read(new XElement("Other"), "");
        invalid.Should().Throw<InvalidDataException>();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void Read_DumpWriterFailureIsReportedWithoutReplacingOriginalError()
    {
        var error = new InvalidOperationException("synthetic original failure");
        var reporter = new UnexpectedErrorReporter(new FailingDumpWriter(), _directory);
        var op = PlanOperatorFactsServiceTests.Parse();
        Action read = () => new PlanOperatorFactsService(new ThrowingReader(error), reporter).Read(op, op.Name.Namespace);
        read.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        var result = reporter.Report(error, "deduplication");
        result.DumpCreated.Should().BeFalse();
        result.Summary.Should().Contain("DUMP 生成失败");
        File.ReadAllText(result.MetadataPath!).Should().Contain("synthetic original failure");
    }

    [Fact]
    public void ExtractionLogs_EnforceDebugAndReleaseLevelsWithoutInputValues()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "facts.log");
        Logger.Shutdown();
        try
        {
            Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var op = PlanOperatorFactsServiceTests.Parse("<IndexScan><Object Table='[secret-table]'/><Predicate><ScalarOperator ScalarString='secret-sql'/></Predicate></IndexScan>", "EstimateRows='secret-invalid'");
            new PlanOperatorFactsService().Read(op, op.Name.Namespace);
            Logger.Error("imp11-error-marker");
            Logger.Critical("imp11-fatal-marker");
        }
        finally { Logger.Shutdown(); }
        string log = File.ReadAllText(path);
        log.Should().Contain("[ERROR]").And.Contain("imp11-error-marker").And.Contain("[CRITICAL]").And.Contain("imp11-fatal-marker")
            .And.NotContain("secret-table").And.NotContain("secret-sql").And.NotContain("secret-invalid");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]").And.Contain("IMP-11");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]");
#endif
    }

    [Theory]
    [InlineData("secret-thread-number")]
    [InlineData("2147483648")]
    [InlineData("-1")]
    public void Read_UnusableThreadIdentityLogsSafelyWithoutRequestingDump(string thread)
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "thread.log");
        var reporter = new RecordingReporter();
        Logger.Shutdown();
        try
        {
            Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var op = PlanOperatorFactsServiceTests.Parse(
                "<RunTimeInformation><RunTimeCountersPerThread ActualRows='20000' ActualExecutions='1'/></RunTimeInformation>");
            op.Element(op.Name.Namespace + "RunTimeInformation")!.Elements().Single().SetAttributeValue("Thread", thread);
            var facts = new PlanOperatorFactsService(unexpectedErrors: reporter).Read(op, op.Name.Namespace);
            facts.OutputRows.Value.Should().Be(20000);
            facts.RowsPerExecution.Value.Should().BeNull();
            reporter.Calls.Should().Be(0);
        }
        finally { Logger.Shutdown(); }
        string log = File.ReadAllText(path);
        log.Should().NotContain("secret-thread-number");
#if DEBUG
        log.Should().Contain("[WARN]").And.Contain("IMP-11: Thread");
#else
        log.Should().BeEmpty();
#endif
    }

    private sealed class ThrowingReader(Exception exception) : IPlanExecutionFactsReader
    {
        public PlanExecutionFacts Read(XElement relOp, XNamespace ns) => throw exception;
    }
    private sealed class FailingDumpWriter : ICrashDumpWriter
    {
        public void Write(string path) => throw new IOException("synthetic disk-full failure");
    }
    private sealed class RecordingReporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; return new(null, null, "test"); }
    }
    public void Dispose()
    {
        Logger.Shutdown();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
