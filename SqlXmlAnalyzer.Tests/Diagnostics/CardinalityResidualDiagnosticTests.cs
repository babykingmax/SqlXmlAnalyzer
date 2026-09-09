using System.Collections;
using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Tests.Rules;
using static SqlXmlAnalyzer.Tests.Rules.CardinalityResidualRuleTests;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class CardinalityResidualDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-IMP14-{Guid.NewGuid():N}");
    private static IPlanAnalyzerRule Rule(int number) => number switch
    {
        4 => new RowEstimateMismatchRule(), 30 => new CardinalityErrorRule(),
        6 => new ResidualPredicateRule(), 34 => new ResidualPredOpRule(),
        _ => throw new ArgumentOutOfRangeException(nameof(number))
    };

    [Theory]
    [InlineData(4)]
    [InlineData(30)]
    [InlineData(6)]
    [InlineData(34)]
    public void UnknownRuleFailure_CreatesValidatedDumpOnceAndContinuesOtherRules(int number)
    {
        var error = new InvalidOperationException("IMP-14 synthetic fact enumeration failure");
        var writer = new CountingWriter();
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        var engine = new RuleEngine(unexpectedErrors: reporter);
        var subject = Rule(number);
        engine.RegisterRule(new FaultingFactsRule(subject, error));
        engine.RegisterRule(new DiagnosticProtocolTests.NativeRule("TEST_AFTER", _ => RuleEvaluation.NoHit()));
        var op = Op("EstimateRows='1'", Counter("ActualRows='5000' ActualRowsRead='50000' ActualExecutions='1'"), Predicate());
        var report = engine.AnalyzeNodeDetailed(op, Ns);
        report.Runs.Select(r => r.Status).Should().Equal(RuleRunStatus.Failed, RuleRunStatus.NoHit);
        report.Runs.Should().OnlyContain(r => r.NodeId == "4");
        var incident = report.Runs[0].FailureDiagnostic!;
        incident.DumpCreated.Should().BeTrue(incident.Failure);
        MinidumpValidator.Validate(incident.DumpPath!);
        File.ReadAllText(incident.MetadataPath!).Should().Contain(subject.RuleId).And.Contain("ThrowingPredicates")
            .And.Contain("IMP-14 synthetic fact enumeration failure");
        engine.AnalyzeNodeDetailed(op, Ns).Runs[0].FailureDiagnostic.Should().BeSameAs(incident);
        writer.Calls.Should().Be(1);
        report.Diagnostics.Should().BeEmpty();
        report.ToLegacyResults().Single().Run!.Status.Should().Be(RuleRunStatus.Failed);
        report.ToLegacyResults().Single().NodeId.Should().Be("4");
        DiagnosticTextFormatter.FormatRun(report.Runs[0]).Should().Contain("局部 NodeId：4");
    }

    [Fact]
    public void DumpWriteFailure_PreservesManagedErrorAndExplicitCaptureFailure()
    {
        var reporter = new UnexpectedErrorReporter(new FailingWriter(), _directory);
        var engine = new RuleEngine(unexpectedErrors: reporter);
        engine.RegisterRule(new FaultingFactsRule(new ResidualPredOpRule(), new InvalidOperationException("original-imp14-error")));
        var run = engine.AnalyzeNodeDetailed(Op(counters: Counter("ActualRows='100' ActualRowsRead='1000'"), payload: Predicate()), Ns).Runs.Single();
        run.Status.Should().Be(RuleRunStatus.Failed);
        run.Reason.Should().Be("original-imp14-error");
        run.FailureDiagnostic!.DumpCreated.Should().BeFalse();
        run.FailureDiagnostic.Summary.Should().Contain("DUMP 生成失败");
        File.ReadAllText(run.FailureDiagnostic.MetadataPath!).Should().Contain("original-imp14-error");
    }

    [Fact]
    public void ExpectedFailureAndCancellation_DoNotRequestDumpOrBecomeNoHit()
    {
        var reporter = new DiagnosticProtocolTests.RecordingReporter();
        var engine = new RuleEngine(unexpectedErrors: reporter);
        engine.RegisterRule(new FaultingFactsRule(new ResidualPredicateRule(), new IOException("expected-storage-failure")));
        engine.AnalyzeNodeDetailed(Op(payload: Predicate()), Ns).Runs.Single().Status.Should().Be(RuleRunStatus.Failed);
        using var cancellation = new CancellationTokenSource();
        var cancelEngine = new RuleEngine(unexpectedErrors: reporter);
        cancelEngine.RegisterRule(new FaultingFactsRule(new ResidualPredicateRule(), new OperationCanceledException(cancellation.Token), cancellation.Cancel));
        Action action = () => cancelEngine.AnalyzeNodeDetailed(Op(payload: Predicate()), Ns, cancellation.Token);
        action.Should().Throw<OperationCanceledException>();
        reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void Logs_EnforceBuildLevelsForActualRulePathsWithoutSqlOrIdentifierValues()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "rules.log");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;
        Logger.Shutdown();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var op = Op("EstimateRows='secret-invalid-estimate'", payload: "<IndexScan><Object Table='[secret-table]'/></IndexScan>" + Predicate("[secret-column]='secret-sql'"));
            Engine(new RowEstimateMismatchRule(), new ResidualPredOpRule()).AnalyzeNodeDetailed(op, Ns);
            var engine = new RuleEngine(unexpectedErrors: new UnexpectedErrorReporter(new FailingWriter(), _directory));
            engine.RegisterRule(new FaultingFactsRule(new ResidualPredicateRule(), new InvalidOperationException("synthetic-imp14-error")));
            engine.AnalyzeNodeDetailed(op, Ns).HasFailures.Should().BeTrue();
        }
        finally
        {
            Logger.Shutdown();
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
        stdout.ToString().Should().BeEmpty();
        foreach (string log in new[] { File.ReadAllText(path), stderr.ToString() })
        {
            log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.Contain("synthetic-imp14-error")
                .And.NotContain("secret-invalid-estimate").And.NotContain("secret-table").And.NotContain("secret-column").And.NotContain("secret-sql");
#if DEBUG
            log.Should().Contain("[DEBUG]").And.Contain("[WARN]").And.Contain("IMP-14:");
#else
            log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    // Exercise the actual built-in evaluator with a failing fact collection. No production
    // failure switches or exception-swallowing implementations are required for this test.
    private sealed class FaultingFactsRule(IPlanAnalyzerRule inner, Exception error, Action? beforeThrow = null) : IPlanAnalyzerRule
    {
        public string RuleId => inner.RuleId;
        public string Name => inner.Name;
        public string Description => inner.Description;
        public RuleMetadata Metadata => inner.Metadata;
        public RuleEvaluation Evaluate(RuleAnalysisContext context) => inner.Evaluate(new RuleAnalysisContext(
            context.Analysis, context.Metadata, context.Location, context.Operator,
            context.Facts! with { Predicates = new ThrowingPredicates(error, beforeThrow) },
            context.LegacyElement, context.Namespace, context.DocumentLocation, context.StatementLocation));
    }
    private sealed class ThrowingPredicates(Exception error, Action? beforeThrow) : IReadOnlyList<string>
    {
        private void Fail() { beforeThrow?.Invoke(); throw error; }
        public int Count { get { Fail(); return 0; } }
        public string this[int index] { get { Fail(); return ""; } }
        public IEnumerator<string> GetEnumerator() { Fail(); return Enumerable.Empty<string>().GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class CountingWriter : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path) { Calls++; new WindowsMiniDumpWriter().Write(path); }
    }
    private sealed class FailingWriter : ICrashDumpWriter
    {
        public void Write(string path) => throw new IOException("synthetic dump writer failure");
    }
    public void Dispose()
    {
        Logger.Shutdown();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
