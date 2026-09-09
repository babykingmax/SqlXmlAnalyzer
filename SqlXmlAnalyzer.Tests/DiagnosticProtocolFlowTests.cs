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

public sealed class DiagnosticProtocolFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-Imp12Flow-" + Guid.NewGuid().ToString("N"));
    public DiagnosticProtocolFlowTests() => Directory.CreateDirectory(_directory);
    private string Save(XDocument document, string name = "plan.sqlplan")
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, document.ToString());
        return path;
    }

    [Fact]
    public void Analyze_SameInputKeepsDiagnosticAndEvidenceAcrossModelNodeCliAndHtml()
    {
        string path = Save(Plan());
        var input = new InputRecognitionService().Load(path);
        var doc = input.Document!;
        var report = new SqlXmlAnalysisEngine().AnalyzeInput(input);
        report.IsSuccess.Should().BeTrue();
        var diagnostic = report.Diagnostics!.Diagnostics.Single(d => d.RuleId == "RULE_004_ESTIMATE_MISMATCH");
        report.Issues.Single(i => i.IssueType == diagnostic.RuleId).Diagnostic.Should().BeSameAs(diagnostic);
        var node = new PlanGraphNodeBuilderService().Build(doc.Descendants(Ns + "RelOp").Single(), Ns, new(2, 100));
        var nodeDiagnostic = node.Diagnostics!.Diagnostics.Single(d => d.RuleId == diagnostic.RuleId);
        var scan = Program.ScanPlanFile(path, null, null, false, new(), default);
        var cliDiagnostic = scan.Diagnostics!.Diagnostics.Single(d => d.RuleId == diagnostic.RuleId);
        nodeDiagnostic.DiagnosticId.Should().Be(diagnostic.DiagnosticId);
        cliDiagnostic.DiagnosticId.Should().Be(diagnostic.DiagnosticId);
        JsonSerializer.Serialize(nodeDiagnostic.Evidence).Should().Be(JsonSerializer.Serialize(diagnostic.Evidence));
        JsonSerializer.Serialize(cliDiagnostic.Evidence).Should().Be(JsonSerializer.Serialize(diagnostic.Evidence));
        string formatted = DiagnosticTextFormatter.FormatDiagnostic(diagnostic);
        node.Warnings.Should().Contain(formatted);
        var html = new AnalysisReportController().BuildPlanHtmlReport(doc, path, Ns, report.Diagnostics);
        html.Sections.SelectMany(s => s.Items).Should().Contain(i => i.Description == formatted);
        html.SummaryText.Should().Contain(DiagnosticTextFormatter.Summary(report.Diagnostics));
        var nodeVm = new SqlXmlAnalyzer.Services.PlanGraphNodeUiActionService()
            .CreateNodeFromRelOp(doc.Descendants(Ns + "RelOp").Single(), Ns, 2, 100);
        nodeVm.Diagnostics!.Diagnostics.Should().Contain(d => d.DiagnosticId == diagnostic.DiagnosticId);
        nodeVm.Warnings.Should().Contain(formatted);
    }

    [Fact]
    public void RuleFailure_RemainsRuleFailureAcrossAnalysisCliGuiAndHtml()
    {
        string path = Save(Plan());
        var input = new InputRecognitionService().Load(path);
        RuleEngine Factory() => Engine(new NativeRule("TEST_FAIL", _ => throw new InvalidOperationException("synthetic fault")));
        var analysis = new SqlXmlAnalysisEngine(ruleEngineFactory: Factory).AnalyzeInput(input);
        analysis.IsSuccess.Should().BeFalse();
        analysis.InputStatus.Should().Be(InputStatus.Success);
        analysis.InputErrorCode.Should().BeNull();
        analysis.AnalysisErrorCode.Should().Be("RULE_EXECUTION_FAILED");
        analysis.Issues.Single().Run!.RuleId.Should().Be("TEST_FAIL");
        analysis.Issues.Single().IssueType.Should().NotBe("PARSE_ERROR");
        var scan = Program.ScanPlanFile(path, null, null, false, new(), default, Factory);
        scan.Status.Should().Be("Failed");
        scan.InputStatus.Should().Be("Success");
        scan.InputErrorCode.Should().BeNull();
        scan.FailureMessage.Should().Contain("TEST_FAIL").And.Contain("RULE_EXECUTION_FAILED").And.NotContain("解析执行计划异常");
        var node = new PlanGraphNodeBuilderService(ruleEngine: Factory())
            .Build(input.Document!.Descendants(Ns + "RelOp").Single(), Ns, new(2, 100));
        node.NodeSeverity.Should().Be("Critical");
        node.Warnings.Should().Contain("[Failed] TEST_FAIL").And.Contain("synthetic fault");
        var html = new AnalysisReportController().BuildPlanHtmlReport(input.Document, path, Ns, analysis.Diagnostics);
        html.Sections.SelectMany(s => s.Items).Should().Contain(i => i.Description.Contains("[Failed] TEST_FAIL"));
        html.SummaryText.Should().Contain("不能据此判断计划健康");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(scan));
        json.RootElement.GetProperty("Diagnostics").GetProperty("Runs")[0].GetProperty("Status").GetString().Should().Be("Failed");
    }

    [Fact]
    public void Failure_CannotBeHiddenByInfoSeverityOverride()
    {
        string path = Save(Plan());
        string configuration = Path.Combine(_directory, "rules.json");
        File.WriteAllText(configuration, """{"Rules":[{"RuleId":"RULE_004_ESTIMATE_MISMATCH","Enabled":true,"SeverityOverride":"Info"}]}""");
        var engine = new RuleEngine(configuration, new RecordingReporter());
        engine.RegisterRule(new NativeRule("RULE_004_ESTIMATE_MISMATCH", _ => throw new InvalidOperationException("synthetic fault")));
        var scan = Program.ScanPlanFile(path, configuration, null, false, new(), default, () => engine);
        scan.Status.Should().Be("Failed");
        scan.Diagnostics!.HasFailures.Should().BeTrue();
        scan.Issues.Single().Run!.Status.Should().Be(RuleRunStatus.Failed);
    }

    [Fact]
    public void CliJson_EstimatedPlanPreservesMissingEvidenceWhileGateRemainsCompatible()
    {
        string path = Save(Plan(runtime: false));
        var (code, output) = DocumentReadContractTests.RunCli("--path", path, "--format", "json");
        code.Should().Be(0, "Passed means no selected gate failed, not that missing evidence was checked");
        using var json = JsonDocument.Parse(output);
        var result = json.RootElement[0];
        result.GetProperty("InputStatus").GetString().Should().Be("Success");
        result.GetProperty("Diagnostics").GetProperty("HasMissingEvidence").GetBoolean().Should().BeTrue();
        result.GetProperty("Diagnostics").GetProperty("Runs").EnumerateArray()
            .Should().Contain(r => r.GetProperty("Status").GetString() == "Skipped"
                && r.GetProperty("ReasonCode").GetString() == "RULE_MISSING_EVIDENCE");
    }

    [Fact]
    public void Failure_BlocksRefactoringAndWritebackAndGuiKeepsOriginalSql()
    {
        var doc = Plan();
        string planPath = Save(doc);
        string sqlPath = Path.Combine(_directory, "source.sql");
        File.WriteAllText(sqlPath, "SELECT 1;");
        RuleEngine Factory() => Engine(new NativeRule("TEST_FAIL", _ => throw new InvalidOperationException("synthetic fault")));
        var files = new PhysicalFileHandler();
        var refactor = new CountingRefactoring();
        var orchestrator = new ApplicationOrchestrator(new SqlXmlAnalysisEngine(ruleEngineFactory: Factory),
            refactor, files, new SilentReporter(), NullLogger<ApplicationOrchestrator>.Instance);
        var result = orchestrator.Execute(sqlPath, planPath);
        result.IsSuccess.Should().BeFalse();
        result.Diagnostics!.HasFailures.Should().BeTrue();
        result.ErrorMessage.Should().Contain("RULE_EXECUTION_FAILED");
        refactor.Calls.Should().Be(0);
        result.Writeback.Should().BeNull();
        File.ReadAllText(sqlPath).Should().Be("SELECT 1;");
        using var temporary = new TemporaryFileManager();
        var gui = new PlanAnalysisService(orchestrator, files, temporary, diagnosticEngineFactory: Factory).Analyze(doc, Ns, planPath);
        gui.RefactoredSql.Should().Be("SELECT 1");
        gui.WarningsText.Should().Contain("自动重构未执行").And.Contain("[Failed] TEST_FAIL");
        refactor.Calls.Should().Be(0);
    }

    [Fact]
    public void JsonRefactorReport_RetainsStructuredPlanDiagnostics()
    {
        var analysis = new SqlXmlAnalysisEngine().AnalyzeInput(new InputRecognitionService().Parse(Plan().ToString()));
        var result = new RefactorResult("SELECT 1", true, [], new RefactorContext("SELECT 1", analysis, true));
        string path = Path.Combine(_directory, "report.json");
        new JsonResultReporter().Report(result, true, path);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        json.RootElement.GetProperty("Diagnostics").GetProperty("DocumentId").GetString().Should().Be(analysis.Diagnostics!.DocumentId);
        json.RootElement.GetProperty("Diagnostics").GetProperty("Diagnostics").GetArrayLength().Should().Be(analysis.Diagnostics.Diagnostics.Count);
    }

    [Fact]
    public void AnalysisCancellation_IsCancelledWithoutDumpOrFalseSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var reporter = new RecordingReporter();
        var engine = new RuleEngine(unexpectedErrors: reporter);
        engine.RegisterRule(new NativeRule("TEST_CANCEL", _ => { cancellation.Cancel(); return RuleEvaluation.NoHit(); }));
        var analysis = new SqlXmlAnalysisEngine(unexpectedErrors: reporter, ruleEngineFactory: () => engine)
            .AnalyzeInput(new InputRecognitionService().Parse(Plan().ToString()), cancellation.Token);
        analysis.IsSuccess.Should().BeFalse();
        analysis.InputStatus.Should().Be(InputStatus.Cancelled);
        analysis.InputErrorCode.Should().Be("INPUT_CANCELLED");
        reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void HtmlReport_RejectsDiagnosticsFromDifferentDocumentRevision()
    {
        var doc = Plan();
        var report = Engine(new RowEstimateMismatchRule()).AnalyzePlanDetailed(doc, Ns);
        doc.Descendants(Ns + "RelOp").Single().SetAttributeValue("EstimateRows", "100");
        var render = () => new AnalysisReportController().BuildPlanHtmlReport(doc, "plan.sqlplan", Ns, report);
        render.Should().Throw<InvalidDataException>().WithMessage("RULE_REPORT_SOURCE_MISMATCH*");
    }

    [Fact]
    public void NativeStatementScope_VisitsEveryKnownStatementIncludingStatementsWithoutOperators()
    {
        var doc = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Fixture);
        var model = PlanIdentityAdapter.GetDocument(doc)!;
        var report = Engine(new NativeRule("TEST_STMT", c => RuleEvaluation.Hit(Observation(c)), RuleScope.Statement))
            .AnalyzePlanDetailed(doc, Ns);
        report.Runs.Should().HaveCount(model.Statements.Count).And.OnlyContain(r => r.Status == RuleRunStatus.Hit);
        report.Runs.Select(r => r.Location.Statement).Should().BeEquivalentTo(model.Statements.Select(s => s.Key));
    }

    [Fact]
    public void LegacySyntaxFailure_RepeatedInvocationsRemainSkippedInsteadOfBecomingNoHit()
    {
        var doc = Plan(operators: 2);
        doc.Descendants(Ns + "StmtSimple").Single().SetAttributeValue("StatementText", "SELECT FROM WHERE");
        int id = 1;
        foreach (var op in doc.Descendants(Ns + "RelOp")) op.SetAttributeValue("NodeId", id++);
        var engine = Engine(new SargableIndexRecommendationRule());
        var report = engine.AnalyzePlanDetailed(doc, Ns);
        report.Runs.Should().ContainSingle().Which.ReasonCode.Should().Be("RULE_MISSING_EVIDENCE");
        var first = doc.Descendants(Ns + "RelOp").First();
        for (int repeat = 0; repeat < 2; repeat++)
            engine.AnalyzeNodeDetailed(first, Ns).Runs.Single().Status.Should().Be(RuleRunStatus.Skipped);
        report.HasFailures.Should().BeFalse();
    }

    [Fact]
    public void DesktopCli_RuleFailureReturnsFailureAndExportsRunReason()
    {
        string path = Save(Plan());
        string output = Path.Combine(_directory, "desktop.txt");
        bool success = CliService.RunAnalysis(path, "txt", output,
            () => Engine(new NativeRule("TEST_FAIL", _ => throw new InvalidOperationException("synthetic desktop failure"))));
        success.Should().BeFalse();
        File.ReadAllText(output).Should().Contain("[Failed] TEST_FAIL").And.Contain("synthetic desktop failure");
    }

    [Fact]
    public void JUnit_EmitsStatusesAndEscapesFailureTextWithoutChangingTheReason()
    {
        const string reason = "failure ]]> <tag> & value";
        string path = Save(Plan());
        var scan = Program.ScanPlanFile(path, null, null, false, new(), default,
            () => Engine(new NativeRule("TEST_FAIL", _ => throw new InvalidOperationException(reason)),
                new NativeRule("TEST_SKIP", _ => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "No counters."))));
        var xml = SafeXmlHelper.ParseSafe(Program.GenerateJUnitOutput([scan], 1.5));
        var failure = xml.Descendants("failure").Single();
        xml.Descendants("testcase").Single().Elements().Select(e => e.Name.LocalName).Should().Equal("failure", "system-out");
        failure.Attribute("type")!.Value.Should().Be("RuleExecutionFailure");
        failure.Value.Should().Contain(reason);
        xml.Descendants("system-out").Single().Value.Should().Contain("[Failed] TEST_FAIL")
            .And.Contain("[Skipped] TEST_SKIP").And.Contain(reason);
        xml.Descendants("testsuite").Single().Attribute("time")!.Value.Should().Be("1.500");
    }

    [Fact]
    public void LegacySyntaxFailure_FixingStatementInvalidatesTheNodeCache()
    {
        var doc = Plan();
        var statement = doc.Descendants(Ns + "StmtSimple").Single();
        var op = doc.Descendants(Ns + "RelOp").Single();
        op.SetAttributeValue("NodeId", "1");
        statement.SetAttributeValue("StatementText", "SELECT FROM WHERE");
        var engine = Engine(new SargableIndexRecommendationRule());
        engine.AnalyzeNodeDetailed(op, Ns).Runs.Single().Status.Should().Be(RuleRunStatus.Skipped);
        statement.SetAttributeValue("StatementText", "SELECT 1");
        engine.AnalyzeNodeDetailed(op, Ns).Runs.Single().Status.Should().Be(RuleRunStatus.NoHit);
    }

    private sealed class CountingRefactoring : IRefactoringEngine
    {
        public int Calls { get; private set; }
        public RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun)
        {
            Calls++;
            return new(sql + " -- rewritten", true, [], new(sql, report, isDryRun));
        }
    }
    private sealed class SilentReporter : IResultReporter
    {
        public void Report(RefactorResult result) { }
        public void Report(RefactorResult result, bool isDryRun, string? outputPath) { }
    }
    public void Dispose() { Logger.Shutdown(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
