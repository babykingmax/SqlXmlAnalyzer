using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Rules;

public sealed class DiagnosticProtocolTests : IDisposable
{
    internal static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-Imp12-" + Guid.NewGuid().ToString("N"));
    public DiagnosticProtocolTests() => Directory.CreateDirectory(_directory);

    internal static XDocument Plan(bool runtime = true, int operators = 1) => SafeXmlHelper.ParseSafe(
        PlanIdentityModelTests.Wrap("<StmtSimple StatementId='1' StatementText='SELECT 1'><QueryPlan>" +
            string.Concat(Enumerable.Range(0, operators).Select(_ =>
                "<RelOp NodeId='0' PhysicalOp='Index Seek' LogicalOp='Index Seek' Parallel='false' EstimateRows='1'>" +
                (runtime ? "<RunTimeInformation><RunTimeCountersPerThread Thread='0' ActualRows='100000' ActualExecutions='2' ActualRowsRead='200000'/></RunTimeInformation>" : "") +
                "</RelOp>")) + "</QueryPlan></StmtSimple>"));

    internal static DiagnosticProposal Observation(RuleAnalysisContext context, string code = "observed-row-count",
        IssueSeverity severity = IssueSeverity.Warning) => new(code, "Observed rows", "An observation, not a root cause.", severity)
    {
        Confidence = DiagnosticConfidence.High,
        Evidence = [new("OutputRows", "100000", "RunTimeCountersPerThread/@ActualRows", context.Location, "rows")],
        Hypotheses = ["Statistics may contribute."], Recommendations = ["Verify the predicate."],
        Applicability = ["Runtime counters required."], Limitations = ["No root cause established."]
    };

    internal static RuleEngine Engine(params IPlanAnalyzerRule[] rules)
    {
        var engine = new RuleEngine(unexpectedErrors: new RecordingReporter());
        foreach (var rule in rules) engine.RegisterRule(rule);
        return engine;
    }

    [Fact]
    public void Analyze_AllOutcomesRetainIdentityAndContinueAfterFailure()
    {
        var reporter = new RecordingReporter();
        var engine = new RuleEngine(unexpectedErrors: reporter);
        engine.RegisterRule(new NativeRule("TEST_FAIL", _ => throw new InvalidOperationException("synthetic failure")));
        engine.RegisterRule(new NativeRule("TEST_HIT", c => RuleEvaluation.Hit(Observation(c))));
        engine.RegisterRule(new NativeRule("TEST_NONE", _ => RuleEvaluation.NoHit()));
        engine.RegisterRule(new NativeRule("TEST_SKIP", _ => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "No counters.")));
        var report = engine.AnalyzePlanDetailed(Plan(), Ns);
        report.Runs.Select(r => r.Status).Should().Equal(RuleRunStatus.Failed, RuleRunStatus.Hit, RuleRunStatus.NoHit, RuleRunStatus.Skipped);
        report.Runs.Should().OnlyContain(r => r.RuleVersion == "2.1.0" && r.Location.Operator != null && r.ElapsedMilliseconds >= 0);
        report.HasFailures.Should().BeTrue();
        report.HasMissingEvidence.Should().BeTrue();
        report.Diagnostics.Should().ContainSingle().Which.Origins.Single().RunId.Should().Be(report.Runs[1].RunId);
        reporter.Operations.Should().ContainSingle().Which.Should().Contain("TEST_FAIL:v2.1.0");
        report.ToLegacyResults().Should().Contain(r => r.Run != null && r.RuleId == "TEST_FAIL" && r.Severity == "Critical");
        DiagnosticTextFormatter.Format(report).Should().Contain("Failed 1").And.Contain("不能据此判断计划健康");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configure_DisabledAndSeverityOverridesUseImmutableSnapshot(bool enabled)
    {
        const string id = "RULE_004_ESTIMATE_MISMATCH";
        string path = Path.Combine(_directory, "rules.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { Rules = new[] { new { RuleId = id, Enabled = enabled, SeverityOverride = "Info" } } }));
        var rule = new NativeRule(id, c => RuleEvaluation.Hit(Observation(c, severity: IssueSeverity.Critical)));
        var engine = new RuleEngine(path);
        engine.RegisterRule(rule);
        // The publicly inspectable loader result must not mutate an already loaded engine.
        engine.ConfigurationLoadResult.Configuration.Rules[0].Enabled = !enabled;
        engine.ConfigurationLoadResult.Configuration.Rules[0].SeverityOverride = "Critical";
        var report = engine.AnalyzePlanDetailed(Plan(), Ns);
        report.Configuration[id].Enabled.Should().Be(enabled);
        report.Configuration[id].SeverityOverride.Should().Be("Info");
        rule.Calls.Should().Be(enabled ? 1 : 0);
        if (enabled) report.Diagnostics.Single().Severity.Should().Be(IssueSeverity.Info);
        else
        {
            report.Runs.Single().Status.Should().Be(RuleRunStatus.Skipped);
            report.Runs.Single().ReasonCode.Should().Be("RULE_DISABLED");
            report.Diagnostics.Should().BeEmpty();
        }
        var change = () => ((IDictionary<string, RuleConfigurationSnapshot>)report.Configuration).Clear();
        change.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Deduplicate_EqualSemanticsAndEvidenceKeepEveryOriginRegardlessOfPresentation()
    {
        var first = new NativeRule("TEST_A", c => RuleEvaluation.Hit(Observation(c) with { Title = "First", Confidence = DiagnosticConfidence.Low }));
        var second = new NativeRule("TEST_B", c => RuleEvaluation.Hit(Observation(c, severity: IssueSeverity.Critical) with { Title = "Second" }));
        var doc = Plan();
        var forward = Engine(first, second).AnalyzePlanDetailed(doc, Ns);
        var reverse = Engine(second, first).AnalyzePlanDetailed(doc, Ns);
        var diagnostic = forward.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Origins.Select(o => o.RuleId).Should().Equal("TEST_A", "TEST_B");
        diagnostic.Severity.Should().Be(IssueSeverity.Critical);
        diagnostic.Confidence.Should().Be(DiagnosticConfidence.Low);
        forward.HitCount.Should().Be(2);
        forward.Runs.Should().OnlyContain(r => r.DiagnosticIds.Single() == diagnostic.DiagnosticId);
        JsonSerializer.Serialize(diagnostic).Should().Be(JsonSerializer.Serialize(reverse.Diagnostics.Single()));
    }

    [Theory]
    [InlineData("semantic")]
    [InlineData("value")]
    [InlineData("unit")]
    public void Deduplicate_DifferentMeaningOrEvidenceRemainsDistinct(string difference)
    {
        var engine = Engine(new NativeRule("TEST_A", c => RuleEvaluation.Hit(Observation(c))),
            new NativeRule("TEST_B", c =>
            {
                var proposal = Observation(c);
                return RuleEvaluation.Hit(difference == "semantic" ? proposal with { SemanticCode = "other" }
                    : proposal with { Evidence = [proposal.Evidence[0] with { Value = difference == "value" ? "42" : "100000", Unit = difference == "unit" ? "bytes" : "rows" }] });
            }));
        engine.AnalyzePlanDetailed(Plan(), Ns).Diagnostics.Should().HaveCount(2);
    }

    [Fact]
    public void Deduplicate_EvidenceOrderAndDuplicatesDoNotChangeSemanticIdentity()
    {
        var engine = Engine(new NativeRule("TEST_A", c =>
        {
            var p = Observation(c);
            return RuleEvaluation.Hit(p with { Evidence = [p.Evidence[0], p.Evidence[0] with { Unit = "bytes" }] });
        }), new NativeRule("TEST_B", c =>
        {
            var p = Observation(c);
            return RuleEvaluation.Hit(p with { Evidence = [p.Evidence[0] with { Unit = "bytes" }, p.Evidence[0], p.Evidence[0]] });
        }));
        engine.AnalyzePlanDetailed(Plan(), Ns).Diagnostics.Should().ContainSingle().Which.Origins.Should().HaveCount(2);
    }

    [Fact]
    public void Deduplicate_RepeatedNodeIdsKeepCompleteLocationsAndRunIdentity()
    {
        var report = Engine(new NativeRule("TEST_A", c => RuleEvaluation.Hit(Observation(c))))
            .AnalyzePlanDetailed(Plan(operators: 2), Ns);
        report.Diagnostics.Should().HaveCount(2);
        report.Diagnostics.Select(d => d.DiagnosticId).Should().OnlyHaveUniqueItems();
        report.Runs.Select(r => r.RunId).Should().OnlyHaveUniqueItems();
        report.Diagnostics.Select(d => d.Location.Operator!.OperatorOrdinal).Should().Equal(1, 2);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("no-evidence")]
    [InlineData("foreign-evidence")]
    [InlineData("severity")]
    [InlineData("confidence")]
    [InlineData("scope")]
    public void Analyze_InvalidRuleContractFailsWithoutPublishingPartialDiagnostics(string fault)
    {
        var reporter = new RecordingReporter();
        var engine = new RuleEngine(unexpectedErrors: reporter);
        engine.RegisterRule(new NativeRule("TEST_BAD", c =>
        {
            var good = Observation(c);
            if (fault == "empty") return RuleEvaluation.Hit();
            if (fault == "null") return null!;
            var bad = fault switch
            {
                "no-evidence" => good with { Evidence = [] },
                "foreign-evidence" => good with { Evidence = [good.Evidence[0] with { Location = c.Location with { DocumentId = "other" } }] },
                "severity" => good with { Severity = (IssueSeverity)999 },
                "confidence" => good with { Confidence = (DiagnosticConfidence)999 },
                _ => good with { Scope = (RuleScope)999 }
            };
            return RuleEvaluation.Hit(good, bad);
        }));
        var report = engine.AnalyzePlanDetailed(Plan(), Ns);
        report.Diagnostics.Should().BeEmpty();
        report.Runs.Single().Status.Should().Be(RuleRunStatus.Failed);
        reporter.Operations.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancel_BeforeOrDuringRuleStopsWithoutDumpOrLaterInvocation(bool duringRule)
    {
        using var cancellation = new CancellationTokenSource();
        var reporter = new RecordingReporter();
        var engine = new RuleEngine(unexpectedErrors: reporter);
        var cancelling = new NativeRule("TEST_CANCEL", c => { c.CancellationToken.Should().Be(cancellation.Token); cancellation.Cancel(); return RuleEvaluation.NoHit(); });
        var later = new NativeRule("TEST_LATER", _ => RuleEvaluation.NoHit());
        engine.RegisterRule(cancelling); engine.RegisterRule(later);
        if (!duringRule) cancellation.Cancel();
        var analyze = () => engine.AnalyzePlanDetailed(Plan(), Ns, cancellationToken: cancellation.Token);
        analyze.Should().Throw<OperationCanceledException>();
        cancelling.Calls.Should().Be(duringRule ? 1 : 0);
        later.Calls.Should().Be(0);
        reporter.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyRule_CannotMutateSourceOrAffectLaterRules(bool nodeEntry)
    {
        var doc = Plan();
        string original = doc.ToString();
        var engine = Engine(new LegacyRule("TEST_MUTATE", (element, _) => { element.SetAttributeValue("EstimateRows", "999"); return null; }),
            new NativeRule("TEST_AFTER", c => { c.Facts!.EstimatedRows.Value.Should().Be(1); return RuleEvaluation.NoHit(); }));
        var report = nodeEntry ? engine.AnalyzeNodeDetailed(doc.Descendants(Ns + "RelOp").First(), Ns) : engine.AnalyzePlanDetailed(doc, Ns);
        report.Runs.Select(r => r.Status).Should().Equal(RuleRunStatus.Failed, RuleRunStatus.NoHit);
        report.Runs[0].Reason.Should().Contain("RULE_SOURCE_MUTATION");
        doc.ToString().Should().Be(original);
    }

    [Fact]
    public void Publish_CopiesAuthorCollectionsAndExposesReadOnlyModel()
    {
        var evidence = new List<DiagnosticEvidence>();
        var hypotheses = new List<string> { "Possible explanation" };
        RuleAnalysisContext? observed = null;
        var report = Engine(new NativeRule("TEST_COPY", c =>
        {
            observed = c; evidence.Add(Observation(c).Evidence[0]);
            return RuleEvaluation.Hit(Observation(c) with { Evidence = evidence, Hypotheses = hypotheses });
        })).AnalyzePlanDetailed(Plan(), Ns);
        evidence.Clear(); hypotheses.Clear();
        var diagnostic = report.Diagnostics.Single();
        diagnostic.Evidence.Should().ContainSingle();
        diagnostic.Hypotheses.Should().ContainSingle();
        var mutate = () => ((IList<DiagnosticEvidence>)diagnostic.Evidence).Clear();
        mutate.Should().Throw<NotSupportedException>();
        var mutateModel = () => ((IList<PlanOperator>)observed!.Analysis.Model.Operators).Clear();
        mutateModel.Should().Throw<NotSupportedException>();
        typeof(RuleAnalysisContext).GetProperties().Should().NotContain(p => typeof(XObject).IsAssignableFrom(p.PropertyType));
        observed!.Analysis.Capabilities.Should().HaveFlag(DocumentCapabilities.RuntimeCounters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownFailure_CreatesValidatedNativeDumpOrPreservesCaptureFailureAndLogs(bool dumpFails)
    {
        Logger.Shutdown();
        string logPath = Path.Combine(_directory, "protocol.log");
        Logger.Initialize(customLogFilePath: logPath);
        var writer = new CountingDumpWriter(dumpFails);
        var reporter = new UnexpectedErrorReporter(writer, Path.Combine(_directory, "dumps"));
        var failure = new InvalidOperationException("IMP12 synthetic rule fault");
        try
        {
            var engine = new RuleEngine(unexpectedErrors: reporter);
            engine.RegisterRule(new NativeRule("TEST_DUMP", _ => throw failure));
            engine.RegisterRule(new NativeRule("TEST_SKIP", _ => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "No evidence.")));
            var report = engine.AnalyzePlanDetailed(Plan(operators: 2), Ns);
            report.FailedCount.Should().Be(2);
            writer.Calls.Should().Be(1, "the same exception instance is captured once even across invocations");
            var incident = report.Runs[0].FailureDiagnostic!;
            incident.DumpCreated.Should().Be(!dumpFails);
            if (!dumpFails) MinidumpValidator.Validate(incident.DumpPath!);
            else incident.Summary.Should().Contain("DUMP 生成失败");
            File.ReadAllText(incident.MetadataPath!).Should().Contain("TEST_DUMP:v2.1.0").And.Contain("IMP12 synthetic rule fault");
            report.Runs[1].FailureDiagnostic.Should().BeSameAs(incident);
            DiagnosticTextFormatter.Format(report).Should().Contain(incident.Summary);
        }
        finally { Logger.Shutdown(); }
        string log = File.ReadAllText(logPath);
        log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
    }

    [Fact]
    public void ExpectedFailure_StillFailedButDoesNotCreateDump()
    {
        var reporter = new RecordingReporter();
        var engine = new RuleEngine(unexpectedErrors: reporter);
        engine.RegisterRule(new NativeRule("TEST_IO", _ => throw new IOException("storage unavailable")));
        var report = engine.AnalyzePlanDetailed(Plan(), Ns);
        report.HasFailures.Should().BeTrue();
        report.Runs.Single().FailureDiagnostic.Should().BeNull();
        reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void FaultyDumpProvider_DoesNotHideOriginalRuleFailure()
    {
        var engine = new RuleEngine(unexpectedErrors: new FailingReporter());
        engine.RegisterRule(new NativeRule("TEST_FAIL", _ => throw new InvalidOperationException("original rule error")));
        var report = engine.AnalyzePlanDetailed(Plan(), Ns);
        report.Runs.Single().Reason.Should().Be("original rule error");
        report.Runs.Single().FailureDiagnostic!.Failure.Should().Contain("诊断组件失败");
    }

    [Fact]
    public void NativeAndLegacyResults_SerializeAsVersionedProtocolWithExplicitStatuses()
    {
        var report = Engine(new RowEstimateMismatchRule()).AnalyzePlanDetailed(Plan(), Ns);
        var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Evidence.Single(e => e.Name == "RowsPerExecution").Value.Should().Be("50000");
        diagnostic.Confidence.Should().Be(DiagnosticConfidence.High);
        diagnostic.Hypotheses.Should().NotBeEmpty();
        diagnostic.Recommendations.Should().NotBeEmpty();
        diagnostic.Limitations.Should().NotBeEmpty();
        diagnostic.LegacyExplanation.Should().BeNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report));
        json.RootElement.GetProperty("ProtocolVersion").GetString().Should().Be("1.0");
        json.RootElement.GetProperty("Runs")[0].GetProperty("Status").GetString().Should().Be("Hit");
        json.RootElement.TryGetProperty("Context", out _).Should().BeFalse();
    }

    [Fact]
    public void EstimatedPlan_MissingEvidenceIsSkippedAndZeroFindingsNeverClaimsHealth()
    {
        var report = Engine(new RowEstimateMismatchRule(), new CardinalityErrorRule(), new ZeroRowActualsRule())
            .AnalyzePlanDetailed(Plan(runtime: false), Ns);
        report.Diagnostics.Should().BeEmpty();
        report.Runs.Should().HaveCount(3).And.OnlyContain(r => r.Status == RuleRunStatus.Skipped && r.ReasonCode == "RULE_MISSING_EVIDENCE");
        DiagnosticTextFormatter.Format(report).Should().Contain("缺少证据").And.NotContain("完美通过");
        var noHit = Engine(new NativeRule("TEST_NONE", _ => RuleEvaluation.NoHit())).AnalyzePlanDetailed(Plan(), Ns);
        DiagnosticTextFormatter.Format(noHit).Should().Contain("不构成计划完全健康的证明");
    }

    [Fact]
    public void BuiltinRule_ExceptionReachesExecutionBoundaryInsteadOfBecomingNoHit()
    {
        var builtin = new RowEstimateMismatchRule();
        var report = Engine(new LegacyRule(builtin.RuleId, (_, ns) => builtin.Analyze(null!, ns)))
            .AnalyzePlanDetailed(Plan(), Ns);
        report.Runs.Single().Status.Should().Be(RuleRunStatus.Failed);
        report.Runs.Single().RuleId.Should().Be(builtin.RuleId);
        report.Runs.Single().FailureDiagnostic.Should().NotBeNull();
    }

    [Fact]
    public void UnrequestedCancellation_IsRuleFailureWithoutCancellingTheWholeAnalysis()
    {
        var report = Engine(new NativeRule("TEST_BAD_CANCEL", _ => throw new OperationCanceledException("unrequested")),
            new NativeRule("TEST_AFTER", _ => RuleEvaluation.NoHit())).AnalyzePlanDetailed(Plan(), Ns);
        report.Runs.Select(r => r.Status).Should().Equal(RuleRunStatus.Failed, RuleRunStatus.NoHit);
        report.Runs[0].FailureDiagnostic.Should().BeNull();
    }

    [Fact]
    public void Analyze_ForeignIdentityModelCannotProduceApparentlySuccessfulEmptyResults()
    {
        var identities = PlanIdentityAdapter.GetDocument(Plan())!;
        var action = () => Engine(new RowEstimateMismatchRule()).AnalyzePlanDetailed(Plan(), Ns, identities);
        action.Should().Throw<InvalidDataException>().WithMessage("RULE_MODEL_SOURCE_MISMATCH*");
    }

    [Fact]
    public void DiagnosticScope_UsesFullOperatorLocationWhenPlanRuleReportsOperatorEvidence()
    {
        var report = Engine(new NativeRule("TEST_PLAN", c => RuleEvaluation.Hit(Observation(c) with
        {
            Scope = RuleScope.Operator,
            Evidence = [new("OutputRows", "100000", "RunTimeCountersPerThread", c.Operator!.Location)]
        }), RuleScope.Plan)).AnalyzePlanDetailed(Plan(), Ns);
        report.Runs.Single().Location.Operator.Should().BeNull();
        report.Diagnostics.Single().Location.Operator.Should().NotBeNull();
        report.Diagnostics.Single().Evidence.Single().Location.Should().Be(report.Diagnostics.Single().Location);
    }

    [Fact]
    public void DiagnosticScope_MissingOperatorCannotBePublishedAsOperatorFinding()
    {
        var doc = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap("<StmtSimple/>"));
        var report = Engine(new NativeRule("TEST_PLAN", c => RuleEvaluation.Hit(Observation(c) with { Scope = RuleScope.Operator }), RuleScope.Plan))
            .AnalyzePlanDetailed(doc, Ns);
        report.HasFailures.Should().BeTrue();
        report.Diagnostics.Should().BeEmpty();
        report.Runs.Single().Reason.Should().Contain("RULE_LOCATION_UNAVAILABLE");
    }

    internal sealed class NativeRule(string id, Func<RuleAnalysisContext, RuleEvaluation> evaluate, RuleScope scope = RuleScope.Operator) : IPlanAnalyzerRule
    {
        public string RuleId => id;
        public string Name => id;
        public string Description => "Test protocol rule";
        public RuleMetadata Metadata => new(id, RuleCategory.Cardinality, scope, "Warning", Description) { Version = "2.1.0" };
        public int Calls { get; private set; }
        public RuleEvaluation Evaluate(RuleAnalysisContext context) { Calls++; return evaluate(context); }
    }

    internal sealed class LegacyRule(string id, Func<XElement, XNamespace, AnalysisResult?> analyze) : IPlanAnalyzerRule
    {
        public string RuleId => id;
        public string Name => id;
        public string Description => "Test legacy rule";
        public RuleMetadata Metadata => new(id, RuleCategory.Cardinality, RuleScope.Operator, "Warning", Description);
        public AnalysisResult? Analyze(XElement element, XNamespace ns) => analyze(element, ns);
    }

    internal sealed class RecordingReporter : IUnexpectedErrorReporter
    {
        public List<string> Operations { get; } = [];
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Operations.Add(operation);
            return new(null, null, "Synthetic capture not requested");
        }
    }
    private sealed class FailingReporter : IUnexpectedErrorReporter
    {
        public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("synthetic diagnostic failure");
    }
    private sealed class CountingDumpWriter(bool fail) : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path)
        {
            Calls++;
            if (fail) throw new IOException("synthetic dump write failure");
            new WindowsMiniDumpWriter().Write(path);
        }
    }

    public void Dispose() { Logger.Shutdown(); if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
