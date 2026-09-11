using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Scoring;
using SqlXmlAnalyzer.Core.Simulation;
using SqlXmlAnalyzer.Tests.Simulation;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class SimulationDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP16-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownEnumerationFailure_ProducesDumpOrCaptureFailureAndNeverPartialResult(bool dumpFails)
    {
        var doc = CostImpactModelTests.Plan();
        var candidate = CostImpactModelTests.Candidates(doc)[0];
        var error = new InvalidOperationException("synthetic-imp16-enumeration-fault");
        var writer = new Writer(dumpFails);
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        CostImpactResult? result = null;
        Action action = () => result = CostImpactSimulator.SimulateCandidates(doc, Fault(candidate, error), CostImpactModelTests.Ns, reporter);
        action.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        result.Should().BeNull();
        var incident = reporter.Report(error, "verify-deduplication");
        writer.Calls.Should().Be(1);
        incident.DumpCreated.Should().Be(!dumpFails);
        if (!dumpFails) MinidumpValidator.Validate(incident.DumpPath!);
        else incident.Summary.Should().Contain("DUMP 生成失败");
        File.ReadAllText(incident.MetadataPath!).Should().Contain("CostImpactSimulator.SimulateCandidates");
    }

    [Fact]
    public void ExpectedInputFailureAndIoFailure_AreNotUnknownIncidents()
    {
        var doc = CostImpactModelTests.Plan();
        var candidate = CostImpactModelTests.Candidates(doc)[0];
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        Action io = () => CostImpactSimulator.SimulateCandidates(doc, Fault(candidate, new IOException("expected-io")), CostImpactModelTests.Ns, reporter);
        io.Should().Throw<IOException>();
        candidate.KeyColumns[0].Name = "[unclosed";
        Action invalid = () => CostImpactSimulator.Simulate(doc, candidate, CostImpactModelTests.Ns, reporter);
        invalid.Should().Throw<InvalidDataException>();
        Action scoring = () => IndexScoringCalculator.Evaluate(candidate, doc, CostImpactModelTests.Ns, reporter);
        scoring.Should().Throw<InvalidDataException>();
        reporter.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScoreProgrammingFailure_CapturesDumpOrFailureWithoutReplacingOriginalException(bool dumpFails)
    {
        var writer = new Writer(dumpFails);
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        Action action = () => IndexScoringCalculator.Evaluate(null!, null, null, reporter);
        var error = action.Should().Throw<ArgumentNullException>().Which;
        var incident = reporter.Report(error, "verify-score-deduplication");
        writer.Calls.Should().Be(1);
        incident.DumpCreated.Should().Be(!dumpFails);
        if (!dumpFails) MinidumpValidator.Validate(incident.DumpPath!);
        else incident.Summary.Should().Contain("DUMP 生成失败");
        File.ReadAllText(incident.MetadataPath!).Should().Contain("IndexScoringCalculator.Evaluate");
    }

    [Fact]
    public void CancellationDuringCandidateEnumeration_IsHonoredWithoutDump()
    {
        var doc = CostImpactModelTests.Plan();
        var candidate = CostImpactModelTests.Candidates(doc)[0];
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        using var cancellation = new CancellationTokenSource();
        IEnumerable<MissingIndexSuggestion> Inputs()
        {
            yield return candidate;
            cancellation.Cancel();
            yield return candidate;
        }
        Action action = () => CostImpactSimulator.SimulateCandidates(doc, Inputs(), CostImpactModelTests.Ns, reporter, cancellation.Token);
        action.Should().Throw<OperationCanceledException>().Which.CancellationToken.Should().Be(cancellation.Token);
        reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void Logs_RespectBuildModeAndKeepSqlAndIdentifiersOutOfDiagnostics()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "simulation.log");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Logger.Shutdown();
        try
        {
            Console.SetOut(stdout); Console.SetError(stderr);
            Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var doc = CostImpactModelTests.Plan();
            doc.Descendants(CostImpactModelTests.Ns + "ScalarOperator").First().SetAttributeValue("ScalarString", "[K]=(1)");
            var candidate = CostImpactModelTests.Candidates(doc)[0];
            CostImpactSimulator.Simulate(doc, candidate, CostImpactModelTests.Ns);
            IndexScoringCalculator.Evaluate(candidate, doc, CostImpactModelTests.Ns);
            CostImpactSimulator.Simulate(null, candidate, CostImpactModelTests.Ns);
            var reporter = new UnexpectedErrorReporter(new Writer(true), _directory);
            Action fault = () => CostImpactSimulator.SimulateCandidates(doc, Fault(candidate, new InvalidOperationException("synthetic-imp16-fault")), CostImpactModelTests.Ns, reporter);
            fault.Should().Throw<InvalidOperationException>();
        }
        finally { Logger.Shutdown(); Console.SetOut(originalOut); Console.SetError(originalError); }
        stdout.ToString().Should().BeEmpty();
        foreach (string log in new[] { File.ReadAllText(path), stderr.ToString() })
        {
            log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("[db]").And.NotContain("[K]=1").And.NotContain("[K]=(1)");
#if DEBUG
            log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    private static IEnumerable<MissingIndexSuggestion> Fault(MissingIndexSuggestion first, Exception error)
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
    public void Dispose() { Logger.Shutdown(); if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
