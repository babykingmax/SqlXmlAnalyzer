using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class PlanComparisonDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP17-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterStatementFailure_ProducesDumpOrExplicitFailure_AndNeverReturnsPartialComparison(bool dumpFails)
    {
        var error = new InvalidOperationException("synthetic-imp17-comparison-failure");
        var writer = new Writer(dumpFails);
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        var reader = new FaultReader(error);
        var controller = new PlanComparisonController(reader, reporter);
        PlanComparisonResult? result = null;
        var snapshot = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 1)
            + PlanComparisonMultiStatementTests.Statement("SELECT 2", 2));
        Action compare = () => result = controller.BuildComparison(snapshot, snapshot, PlanComparisonMultiStatementTests.Ns);
        compare.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        result.Should().BeNull(); reader.Calls.Should().Be(5, "故障发生在第二条语句");
        var incident = reporter.Report(error, "verify-deduplication");
        writer.Calls.Should().Be(1); incident.DumpCreated.Should().Be(!dumpFails);
        if (dumpFails) incident.Summary.Should().Contain("DUMP 生成失败");
        else MinidumpValidator.Validate(incident.DumpPath!);
        File.ReadAllText(incident.MetadataPath!).Should().Contain("PlanComparisonController.BuildComparison");
    }

    [Fact]
    public void CancellationAndStaleSelection_DoNotCreateUnknownIncidents()
    {
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        var controller = new PlanComparisonController(unexpectedErrors: reporter);
        var snapshot = PlanComparisonMultiStatementTests.RuntimeSnapshot(1);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Action cancelled = () => controller.BuildComparison(snapshot, snapshot, PlanComparisonMultiStatementTests.Ns, cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
        snapshot.SelectedQueryPlan = snapshot.IdentityModel!.QueryPlans.Single().Key;
        snapshot.Document.Root!.SetAttributeValue("Build", "17.0.1.1");
        Action stale = () => controller.BuildComparison(snapshot, snapshot, PlanComparisonMultiStatementTests.Ns);
        stale.Should().Throw<InvalidDataException>(); reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void LogsFollowBuildPolicy_WithoutSqlParameterValuesOrObjectNames()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "comparison.log");
        var stdout = new StringWriter(); var stderr = new StringWriter();
        var previousOut = Console.Out; var previousError = Console.Error;
        Logger.Shutdown();
        try
        {
            Console.SetOut(stdout); Console.SetError(stderr);
            Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var snapshot = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT secret_value FROM secret_table", 1, "secret_table"));
            new PlanComparisonController().BuildComparison(snapshot, snapshot, PlanComparisonMultiStatementTests.Ns);
            var fault = new PlanComparisonController(new FaultReader(new InvalidOperationException("synthetic-imp17-failure"), 1),
                new UnexpectedErrorReporter(new Writer(true), _directory));
            Action action = () => fault.BuildComparison(snapshot, snapshot, PlanComparisonMultiStatementTests.Ns);
            action.Should().Throw<InvalidOperationException>();
        }
        finally { Logger.Shutdown(); Console.SetOut(previousOut); Console.SetError(previousError); }
        stdout.ToString().Should().BeEmpty();
        foreach (string output in new[] { File.ReadAllText(path), stderr.ToString() })
        {
            output.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("secret_table").And.NotContain("secret_value");
#if DEBUG
            output.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            output.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    private sealed class FaultReader(Exception error, int failOnCall = 5) : IPlanComparisonRuntimeMetricsReader
    {
        public int Calls { get; private set; }
        public PlanComparisonRuntimeMetrics Read(XElement element) => ++Calls == failOnCall ? throw error : new PlanComparisonRuntimeMetricsService().Read(element);
    }
    private sealed class Writer(bool fail) : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path) { Calls++; if (fail) throw new IOException("synthetic dump failure"); new WindowsMiniDumpWriter().Write(path); }
    }
    public void Dispose()
    {
        Logger.Shutdown();
        if (!Path.GetFullPath(_directory).StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP17-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test cleanup target");
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
