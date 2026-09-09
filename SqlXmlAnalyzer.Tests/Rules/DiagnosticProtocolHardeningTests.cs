using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using static SqlXmlAnalyzer.Tests.Rules.DiagnosticProtocolTests;

namespace SqlXmlAnalyzer.Tests.Rules;

public sealed class DiagnosticProtocolHardeningTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-Imp12Hardening-" + Guid.NewGuid().ToString("N"));

    public DiagnosticProtocolHardeningTests() => Directory.CreateDirectory(_directory);

    internal static (IPlanAnalyzerRule Rule, XDocument Document, string ResultId, IssueSeverity Severity) Branch(string branch)
    {
        (IPlanAnalyzerRule rule, string payload, string parallel, string resultId, IssueSeverity severity) = branch switch
        {
            "parallel" => ((IPlanAnalyzerRule)new ParallelSkewRule(), "<RunTimeInformation>" +
                string.Concat(Enumerable.Range(1, 8).Select(i => $"<RunTimeCountersPerThread Thread='{i}' ActualRows='50' ActualExecutions='1'/>")) +
                "</RunTimeInformation>", "true", "RULE_010_INEFFECTIVE_PARALLELISM", IssueSeverity.Info),
            "residual" => (new ResidualPredicateRule(), "<IndexScan><Predicate><ScalarOperator ScalarString='YEAR([t].[d])=(2026)'/></Predicate></IndexScan>",
                "false", "RULE_007_NON_SARGABLE", IssueSeverity.Warning),
            "case" => (new AntiPatternRule(), "<IndexScan><Predicate><ScalarOperator ScalarString='CASE WHEN [t].[a]=(1) THEN [t].[b] ELSE [t].[c] END=(2)'/></Predicate></IndexScan>",
                "false", "RULE_013_CASE_IN_PREDICATE", IssueSeverity.Warning),
            "parameter" => (new ParameterSniffingRule(), "<IndexScan><Predicate><ScalarOperator ScalarString='[t].[a]=&quot;OPTIMIZE FOR UNKNOWN&quot;'/></Predicate></IndexScan>",
                "false", "RULE_003_OPTIMIZE_FOR_UNKNOWN", IssueSeverity.Info),
            _ => throw new ArgumentOutOfRangeException(nameof(branch))
        };
        return (rule, SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
            $"<StmtSimple StatementId='1'><QueryPlan><RelOp NodeId='0' PhysicalOp='Index Scan' Parallel='{parallel}' EstimateRows='400'>{payload}</RelOp></QueryPlan></StmtSimple>")), resultId, severity);
    }

    [Theory]
    [InlineData("parallel", false)]
    [InlineData("parallel", true)]
    [InlineData("residual", false)]
    [InlineData("residual", true)]
    [InlineData("case", false)]
    [InlineData("case", true)]
    [InlineData("parameter", false)]
    [InlineData("parameter", true)]
    public void LegacyBranch_PreservesResultIdAndOwnerWithoutFailureOrDump(string branch, bool nodeEntry)
    {
        var (rule, document, resultId, severity) = Branch(branch);
        var reporter = new RecordingReporter();
        var engine = new RuleEngine(unexpectedErrors: reporter);
        engine.RegisterRule(rule);
        var report = nodeEntry ? engine.AnalyzeNodeDetailed(document.Descendants(Ns + "RelOp").Single(), Ns)
            : engine.AnalyzePlanDetailed(document, Ns);

        report.HasFailures.Should().BeFalse();
        report.Runs.Should().ContainSingle().Which.Status.Should().Be(RuleRunStatus.Hit);
        report.Runs.Single().RuleId.Should().Be(rule.RuleId);
        var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.RuleId.Should().Be(resultId);
        diagnostic.SemanticCode.Should().Be(resultId);
        diagnostic.Origins.Single().RuleId.Should().Be(rule.RuleId);
        diagnostic.Severity.Should().Be(severity);
        report.ToLegacyResults().Single().RuleId.Should().Be(resultId);
        DiagnosticTextFormatter.FormatDiagnostic(diagnostic).Should().Contain(resultId).And.Contain(rule.RuleId);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report));
        json.RootElement.GetProperty("Diagnostics")[0].GetProperty("RuleId").GetString().Should().Be(resultId);
        reporter.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("parallel", false)]
    [InlineData("parallel", true)]
    [InlineData("residual", false)]
    [InlineData("residual", true)]
    [InlineData("case", false)]
    [InlineData("case", true)]
    [InlineData("parameter", false)]
    [InlineData("parameter", true)]
    public void LegacyBranch_UsesOwnerEnablementAndSeverityConfiguration(string branch, bool enabled)
    {
        var (rule, document, resultId, _) = Branch(branch);
        string path = Path.Combine(_directory, "rules.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { Rules = new[] { new { rule.RuleId, Enabled = enabled, SeverityOverride = "Critical" } } }));
        var reporter = new RecordingReporter();
        var engine = new RuleEngine(path, reporter);
        engine.RegisterRule(rule);
        var report = engine.AnalyzePlanDetailed(document, Ns);
        report.HasFailures.Should().BeFalse();
        if (enabled)
        {
            report.Diagnostics.Single().RuleId.Should().Be(resultId);
            report.Diagnostics.Single().Severity.Should().Be(IssueSeverity.Critical);
        }
        else
        {
            report.Runs.Single().ReasonCode.Should().Be("RULE_DISABLED");
            report.Diagnostics.Should().BeEmpty();
        }
        reporter.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("TEST_OWNER", "TEST_OTHER")]
    [InlineData("RULE_006_RESIDUAL_PREDICATE", "RULE_007_NON_SARGABLE")]
    [InlineData("TEST_OWNER", "")]
    public void LegacyBranch_RejectsUnknownOrImpersonatedResultIds(string owner, string resultId)
    {
        var reporter = new RecordingReporter();
        var engine = new RuleEngine(unexpectedErrors: reporter);
        engine.RegisterRule(new LegacyRule(owner, (_, _) => new AnalysisResult { RuleId = resultId, Severity = "Info" }));
        engine.RegisterRule(new NativeRule("TEST_NEXT", _ => RuleEvaluation.NoHit()));
        var report = engine.AnalyzePlanDetailed(Plan(), Ns);
        report.Runs.Select(r => r.Status).Should().Equal(RuleRunStatus.Failed, RuleRunStatus.NoHit);
        report.Runs[0].Reason.Should().Contain("RULE_RESULT_ID_MISMATCH");
        report.Diagnostics.Should().BeEmpty();
        reporter.Operations.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ThreadSkew_SerialOperatorsAreNotApplicableEvenWithoutRuntime(bool runtime, bool nodeEntry)
    {
        var document = Plan(runtime);
        var engine = Engine(new ThreadSkewRule(), new ParallelSkewRule());
        var report = nodeEntry ? engine.AnalyzeNodeDetailed(document.Descendants(Ns + "RelOp").Single(), Ns)
            : engine.AnalyzePlanDetailed(document, Ns);
        report.Runs.Should().HaveCount(2).And.OnlyContain(r => r.Status == RuleRunStatus.Skipped && r.ReasonCode == "RULE_NOT_APPLICABLE");
        report.HasMissingEvidence.Should().BeFalse();
        report.HasFailures.Should().BeFalse();
    }

    [Theory]
    [InlineData("<RunTimeCountersPerThread Thread='1' ActualRows='0'/><RunTimeCountersPerThread Thread='2' ActualRows='0'/>", false)]
    [InlineData("<RunTimeCountersPerThread Thread='1' ActualRows='0'/><RunTimeCountersPerThread Thread='2'/>", true)]
    [InlineData("<RunTimeCountersPerThread Thread='1' ActualRows='0'/><RunTimeCountersPerThread Thread='1' ActualRows='0'/>", true)]
    [InlineData("<RunTimeCountersPerThread Thread='0' ActualRows='0'/>", true)]
    [InlineData("", true)]
    public void ThreadSkew_ParallelCountersDistinguishZeroFromIncompleteEvidence(string counters, bool missing)
    {
        var document = Plan(runtime: false);
        var op = document.Descendants(Ns + "RelOp").Single();
        op.SetAttributeValue("Parallel", "true");
        op.Add(SafeXmlHelper.ParseSafe($"<RunTimeInformation xmlns='{Ns}'>{counters}</RunTimeInformation>").Root!);
        var report = Engine(new ThreadSkewRule(), new ParallelSkewRule()).AnalyzePlanDetailed(document, Ns);
        report.HasMissingEvidence.Should().Be(missing);
        report.Runs.Should().OnlyContain(r => r.Status == (missing ? RuleRunStatus.Skipped : RuleRunStatus.NoHit));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capabilities_DefaultAndExplicitInputAgreeForPlanAndNode(bool runtime)
    {
        var input = new InputRecognitionService().Parse(Plan(runtime).ToString());
        var document = input.Document!;
        var engine = Engine(new NativeRule("TEST_SOURCE", c => c.Analysis.Capabilities.HasFlag(DocumentCapabilities.PreservedSource)
            ? RuleEvaluation.NoHit() : RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "Source unavailable")));
        var inferred = engine.AnalyzePlanDetailed(document, Ns);
        var supplied = engine.AnalyzePlanDetailed(document, Ns, capabilities: input.Capabilities);
        var node = engine.AnalyzeNodeDetailed(document.Descendants(Ns + "RelOp").Single(), Ns);
        inferred.Capabilities.Should().Be(input.Capabilities);
        node.Capabilities.Should().Be(input.Capabilities);
        inferred.Runs.Single().Status.Should().Be(supplied.Runs.Single().Status).And.Be(RuleRunStatus.NoHit);
        node.Runs.Single().Status.Should().Be(RuleRunStatus.NoHit);
    }

    [Fact]
    public void Capabilities_ExplicitRestrictionIsNotSilentlyExpanded()
    {
        Engine(new RowEstimateMismatchRule()).AnalyzePlanDetailed(Plan(), Ns, capabilities: DocumentCapabilities.None)
            .Capabilities.Should().Be(DocumentCapabilities.None);
    }

    [Fact]
    public void LegacyBranch_MergeUsesOwnerOrderingAndRetainsMatchingVersion()
    {
        var (rule, document, resultId, _) = Branch("residual");
        var evidence = Engine(rule).AnalyzePlanDetailed(document, Ns).Diagnostics.Single().Evidence;
        var native = new NativeRule("RULE_006_Z", c => RuleEvaluation.Hit(new DiagnosticProposal(resultId, "Native", "Same observation", IssueSeverity.Warning)
        {
            Evidence = evidence
        }));
        var forward = Engine(rule, native).AnalyzePlanDetailed(document, Ns).Diagnostics.Single();
        var reverse = Engine(native, rule).AnalyzePlanDetailed(document, Ns).Diagnostics.Single();
        forward.RuleId.Should().Be(resultId);
        forward.RuleVersion.Should().Be("2.0.0");
        forward.Origins.Select(o => o.RuleId).Should().Equal(rule.RuleId, native.RuleId);
        JsonSerializer.Serialize(forward).Should().Be(JsonSerializer.Serialize(reverse));
    }

    [Fact]
    public void LegacyResult_ChangingAuthorResultCannotChangePublishedIdentity()
    {
        var result = new AnalysisResult { RuleId = "TEST_OWNER", Title = "Original", Message = "Original", Severity = "Info" };
        var report = Engine(new LegacyRule("TEST_OWNER", (_, _) => result)).AnalyzePlanDetailed(Plan(), Ns);
        result.RuleId = "OTHER";
        result.Message = "Changed";
        report.Diagnostics.Single().RuleId.Should().Be("TEST_OWNER");
        report.ToLegacyResults().Single().Message.Should().Be("Original");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
