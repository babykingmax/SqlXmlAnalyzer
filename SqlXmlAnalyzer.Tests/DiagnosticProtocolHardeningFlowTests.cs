using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlXmlAnalyzer.Analysis;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Rules;
using static SqlXmlAnalyzer.Tests.Rules.DiagnosticProtocolTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class DiagnosticProtocolHardeningFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-Imp12HardeningFlow-" + Guid.NewGuid().ToString("N"));
    public DiagnosticProtocolHardeningFlowTests() => Directory.CreateDirectory(_directory);

    private string Save(XDocument document)
    {
        string path = Path.Combine(_directory, "plan.sqlplan");
        File.WriteAllText(path, document.ToString());
        return path;
    }

    [Theory]
    [InlineData("parallel")]
    [InlineData("residual")]
    [InlineData("case")]
    [InlineData("parameter")]
    public void LegacyBranch_KeepsResultAndOriginAcrossAnalysisNodeCliGuiAndHtml(string branch)
    {
        var (rule, document, resultId, severity) = DiagnosticProtocolHardeningTests.Branch(branch);
        string path = Save(document);
        var input = new InputRecognitionService().Load(path);
        RuleEngine Factory() => Engine(rule);
        var analysis = new SqlXmlAnalysisEngine(ruleEngineFactory: Factory).AnalyzeInput(input);
        analysis.IsSuccess.Should().BeTrue();
        analysis.Issues.Single().IssueType.Should().Be(resultId);
        var node = new PlanGraphNodeBuilderService(ruleEngine: Factory()).Build(input.Document!.Descendants(Ns + "RelOp").Single(), Ns, new(2, 100));
        var scan = Program.ScanPlanFile(path, null, null, false, new(), default, Factory);
        scan.Status.Should().Be("Passed");
        scan.Issues.Single().RuleId.Should().Be(resultId);
        using var temporary = new TemporaryFileManager();
        var files = new PhysicalFileHandler();
        var orchestrator = new ApplicationOrchestrator(new SqlXmlAnalysisEngine(ruleEngineFactory: Factory),
            new NoRefactor(), files, new SilentReporter(), NullLogger<ApplicationOrchestrator>.Instance);
        var gui = new PlanAnalysisService(orchestrator, files, temporary, diagnosticEngineFactory: Factory).Analyze(input.Document, Ns, path);
        foreach (var report in new[] { analysis.Diagnostics!, node.Diagnostics!, scan.Diagnostics!, gui.Diagnostics! })
        {
            report.HasFailures.Should().BeFalse();
            report.Diagnostics.Single().RuleId.Should().Be(resultId);
            report.Diagnostics.Single().Origins.Single().RuleId.Should().Be(rule.RuleId);
            report.Diagnostics.Single().Severity.Should().Be(severity);
            report.Capabilities.Should().Be(input.Capabilities);
            report.Diagnostics.Single().DiagnosticId.Should().Be(analysis.Diagnostics!.Diagnostics.Single().DiagnosticId);
        }
        node.Warnings.Should().Contain(resultId);
        gui.WarningsText.Should().Contain(resultId).And.NotContain("自动重构未执行");
        var html = new AnalysisReportController().BuildPlanHtmlReport(input.Document, path, Ns, gui.Diagnostics);
        html.Sections.SelectMany(s => s.Items).Should().Contain(i => i.Description.Contains(resultId) && i.Description.Contains(rule.RuleId));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(scan));
        json.RootElement.GetProperty("Diagnostics").GetProperty("Diagnostics")[0].GetProperty("RuleId").GetString().Should().Be(resultId);
    }

    [Theory]
    [InlineData("no-hit")]
    [InlineData("disabled")]
    [InlineData("not-applicable")]
    [InlineData("missing-evidence")]
    public void Node_ExecutionStatusDoesNotCreatePerformanceWarning(string status)
    {
        string config = Path.Combine(_directory, "rules.json");
        File.WriteAllText(config, "{\"Rules\":[{\"RuleId\":\"RULE_004_ESTIMATE_MISMATCH\",\"Enabled\":" + (status == "disabled" ? "false" : "true") + "}]}");
        var engine = new RuleEngine(config, new RecordingReporter());
        engine.RegisterRule(new NativeRule("RULE_004_ESTIMATE_MISMATCH", _ => status switch
        {
            "not-applicable" => RuleEvaluation.Skipped("RULE_NOT_APPLICABLE", "Serial operator"),
            "missing-evidence" => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "No counters"),
            _ => RuleEvaluation.NoHit()
        }));
        var op = CleanPlan().Descendants(Ns + "RelOp").Single();
        var node = new PlanGraphNodeBuilderService(ruleEngine: engine).Build(op, Ns, new(2, 100));
        var vm = new PlanNodeViewModel { Warnings = node.Warnings, DiagnosticStatusText = node.DiagnosticStatusText, Diagnostics = node.Diagnostics };
        node.Warnings.Should().BeEmpty();
        node.NodeSeverity.Should().Be("Info");
        vm.HasWarningVisible.Should().Be("Collapsed");
        vm.HasDiagnosticStatusVisible.Should().Be("Visible");
        vm.DiagnosticStatusText.Should().Contain(status == "no-hit" ? "NoHit 1" : "Skipped 1");
        if (status == "missing-evidence") vm.DiagnosticStatusText.Should().Contain("RULE_MISSING_EVIDENCE");
    }

    [Fact]
    public void Node_DefaultRulesAndUiMappingPreserveMissingSqlStatusWithoutFalseWarning()
    {
        var op = CleanPlan().Descendants(Ns + "RelOp").Single();
        var vm = new SqlXmlAnalyzer.Services.PlanGraphNodeUiActionService().CreateNodeFromRelOp(op, Ns, 2, 100);
        vm.Warnings.Should().BeEmpty();
        vm.HasWarningVisible.Should().Be("Collapsed");
        vm.DiagnosticStatusText.Should().Contain("Hit 0").And.Contain("RULE_NOT_APPLICABLE");
        vm.HasDiagnosticStatusVisible.Should().Be("Visible");
        vm.Diagnostics!.HasMissingEvidence.Should().BeTrue();
        vm.Diagnostics.Runs.Single(run => run.RuleId == "RULE_024_SCALAR_SUBQUERY_PATTERN")
            .ReasonCode.Should().Be("RULE_MISSING_EVIDENCE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Node_RealDiagnosticOrFailureStillDisplaysWarning(bool failure)
    {
        var engine = Engine(new NativeRule("TEST_WARNING", c => failure
            ? throw new InvalidOperationException("Synthetic failure") : RuleEvaluation.Hit(Observation(c))));
        var op = CleanPlan().Descendants(Ns + "RelOp").Single();
        var node = new PlanGraphNodeBuilderService(ruleEngine: engine).Build(op, Ns, new(2, 100));
        var vm = new PlanNodeViewModel { Warnings = node.Warnings, DiagnosticStatusText = node.DiagnosticStatusText };
        vm.HasWarningVisible.Should().Be("Visible");
        node.NodeSeverity.Should().Be(failure ? "Critical" : "Warning");
        vm.DiagnosticStatusText.Should().Contain(failure ? "Failed 1" : "Hit 1");
    }

    [Fact]
    public void Capabilities_NativeRuleSeesPreservedSourceAtAllEntrypoints()
    {
        string path = Save(CleanPlan());
        var input = new InputRecognitionService().Load(path);
        RuleEngine Factory() => Engine(new NativeRule("TEST_SOURCE", c => c.Analysis.Capabilities.HasFlag(DocumentCapabilities.PreservedSource)
            ? RuleEvaluation.NoHit() : RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "Source unavailable")));
        var files = new PhysicalFileHandler();
        var orchestrator = new ApplicationOrchestrator(new SqlXmlAnalysisEngine(ruleEngineFactory: Factory),
            new NoRefactor(), files, new SilentReporter(), NullLogger<ApplicationOrchestrator>.Instance);
        using var temporary = new TemporaryFileManager();
        var gui = new PlanAnalysisService(orchestrator, files, temporary, diagnosticEngineFactory: Factory).Analyze(input.Document!, Ns, path);
        var cli = Program.ScanPlanFile(path, null, null, false, new(), default, Factory);
        var node = new PlanGraphNodeBuilderService(ruleEngine: Factory()).Build(input.Document!.Descendants(Ns + "RelOp").Single(), Ns, new(2, 100));
        foreach (var report in new[] { gui.Diagnostics!, cli.Diagnostics!, node.Diagnostics! })
        {
            report.Capabilities.Should().Be(input.Capabilities);
            report.Runs.Single().Status.Should().Be(RuleRunStatus.NoHit);
            report.HasMissingEvidence.Should().BeFalse();
        }
    }

    [Fact]
    public void Capabilities_SourceMutationRefreshesFactsWithoutLosingPreservedSource()
    {
        var document = CleanPlan();
        var engine = Engine(new ThreadSkewRule());
        var before = engine.AnalyzePlanDetailed(document, Ns);
        document.Descendants(Ns + "RunTimeInformation").Single().Remove();
        var after = engine.AnalyzeNodeDetailed(document.Descendants(Ns + "RelOp").Single(), Ns);
        before.Capabilities.Should().HaveFlag(DocumentCapabilities.RuntimeCounters);
        after.Capabilities.Should().NotHaveFlag(DocumentCapabilities.RuntimeCounters).And.HaveFlag(DocumentCapabilities.PreservedSource);
        after.DocumentId.Should().NotBe(before.DocumentId);
    }

    internal static XDocument CleanPlan() => SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
        "<StmtSimple StatementId='1'><QueryPlan><RelOp NodeId='0' PhysicalOp='Constant Scan' LogicalOp='Constant Scan' Parallel='false' EstimateRows='1'>" +
        "<RunTimeInformation><RunTimeCountersPerThread Thread='0' ActualRows='1' ActualRowsRead='1' ActualExecutions='1'/></RunTimeInformation>" +
        "<ConstantScan/></RelOp></QueryPlan></StmtSimple>"));

    private sealed class NoRefactor : IRefactoringEngine
    {
        public RefactorResult Run(string sql, AnalysisReport analysis, RefactorOptions options, bool isDryRun) => throw new InvalidOperationException("No SQL text supplied; refactoring must not run");
    }
    private sealed class SilentReporter : IResultReporter
    {
        public void Report(RefactorResult result) { }
        public void Report(RefactorResult result, bool isDryRun, string? outputPath = null) { }
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
