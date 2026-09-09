using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlXmlAnalyzer.Analysis;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanIdentityFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp10Flow-{Guid.NewGuid():N}");
    private static XNamespace Ns => PlanIdentityModelTests.Ns;
    public PlanIdentityFlowTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyReRecognition_PreservesUnchangedDocumentIdentityAndAvailableProvenance(bool fromBytes)
    {
        var reader = new InputRecognitionService();
        var input = fromBytes ? await reader.ReadFileAsync(WriteFixture()) : reader.Parse(PlanIdentityModelTests.Fixture);
        var model = PlanIdentityAdapter.GetDocument(input.Document)!;
        var again = InputRecognitionService.Recognize(input.Document);
        again.Envelope.Should().BeSameAs(input.Envelope);
        new SqlXmlAnalysisEngine().AnalyzeDocument(input.Document!).Plan.Should().BeSameAs(model);
        PlanIdentityAdapter.GetDocument(input.Document).Should().BeSameAs(model);
        model.Envelope.SourceHash.Should().Be(input.Envelope!.SourceHash);
        if (fromBytes) model.Envelope.SourceHash.Should().NotBeNull();
    }

    [Fact]
    public async Task GuiReadAndAnalysis_PreserveTheSameDocumentAndIssueOwnership()
    {
        string path = WriteFixture();
        File.WriteAllText(path, PlanIdentityModelTests.Fixture.Replace("EstimateRows=\"1\"", "EstimateRows=\"10000\"", StringComparison.Ordinal));
        var opened = await new DocumentOpenService().OpenAsync(path);
        opened.IsSuccess.Should().BeTrue();
        var model = PlanIdentityAdapter.GetDocument(opened.Document)!;
        var report = new SqlXmlAnalysisEngine().AnalyzeInput(opened.Input!);
        report.IsSuccess.Should().BeTrue();
        report.Plan.Should().BeSameAs(model);
        report.Plan!.Envelope.Should().BeSameAs(opened.Input!.Envelope);
        report.Issues.Should().NotBeEmpty();
        report.Issues.Should().OnlyContain(issue => issue.Location != null &&
            issue.Location.DocumentId == model.Envelope.DocumentId);
        var operatorIssues = report.Issues.Where(issue => issue.Location!.Operator != null).ToList();
        operatorIssues.Should().NotBeEmpty();
        operatorIssues.Should().OnlyContain(issue => model.GetOperatorSource(issue.Location!.Operator!) != null);
    }

    [Fact]
    public void RuleScopes_KeepDocumentStatementAndOperatorLocationsSeparate()
    {
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Fixture);
        var model = PlanIdentityAdapter.GetDocument(document)!;
        var engine = new RuleEngine();
        foreach (var scope in Enum.GetValues<RuleScope>()) engine.RegisterRule(new ScopeRule(scope));
        var results = engine.AnalyzePlan(document, Ns);
        results.Where(r => r.Metadata!.Scope == RuleScope.Plan).Should().ContainSingle().Which
            .Location!.Statement.Should().BeNull();
        var statementResults = results.Where(r => r.Metadata!.Scope == RuleScope.Statement).ToList();
        statementResults.Should().HaveCount(2, "legacy statement rules currently consume StmtSimple");
        statementResults.Select(r => r.Location!.Statement).Should().OnlyHaveUniqueItems();
        statementResults.Should().OnlyContain(r => r.Location!.Operator == null);
        var operators = results.Where(r => r.Metadata!.Scope == RuleScope.Operator).ToList();
        operators.Select(r => r.Location!.Operator).Should().Equal(model.Operators.Select(op => op.Key));
        operators[0].Objects.Should().BeEmpty();
        operators[1].Objects.Single().Identity.Schema.Should().Be("sales");
        operators[3].Objects.Single().Identity.Schema.Should().Be("audit");
    }

    [Fact]
    public void SyntheticRuleContexts_DoNotMutateTheSourceOrBorrowNestedStatementIdentity()
    {
        var input = new InputRecognitionService().Parse(PlanIdentityModelTests.Wrap(
            "<StmtSimple StatementText='outer'><UDF><Statements><StmtSimple StatementText='inner'><QueryPlan><RelOp NodeId='0'/></QueryPlan></StmtSimple></Statements></UDF></StmtSimple>"));
        var model = PlanIdentityAdapter.GetDocument(input.Document)!;
        string before = input.Document!.ToString(SaveOptions.DisableFormatting);
        var engine = new RuleEngine();
        engine.RegisterRule(new ScopeRule(RuleScope.Statement));
        var results = engine.AnalyzePlan(input.Document, Ns);
        results.Select(r => r.Location!.Statement).Should().Equal(model.Statements.Select(s => s.Key));
        results[0].NodeId.Should().BeEmpty();
        input.Document.ToString(SaveOptions.DisableFormatting).Should().Be(before);
        PlanIdentityAdapter.GetDocument(input.Document).Should().BeSameAs(model);
        model.GetOperatorSource(model.Operators.Single().Key).Should().NotBeNull();
    }

    [Fact]
    public void GraphLoad_DuplicateNodeIdsHaveDistinctSelectionAndOwnObjects()
    {
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Fixture);
        var graph = LoadGraph(document);
        graph.MasterNodes.Should().HaveCount(6);
        graph.MasterConnections.Should().HaveCount(2);
        graph.MasterNodes.Select(n => n.SelectionKey).Should().OnlyHaveUniqueItems();
        graph.MasterNodes.Where(n => n.PhysicalOp == "Nested Loops").Should().OnlyContain(n =>
            n.ObjectReferences.Count == 0 && n.TableName == "" && n.AssociatedSuggestion == null);
        var sales = graph.MasterNodes.Single(n => n.ObjectReferences.Count == 1 &&
            n.ObjectReferences[0].Alias == "s");
        var audit = graph.MasterNodes.Single(n => n.ObjectReferences.Count == 1 &&
            n.ObjectReferences[0].Alias == "a");
        sales.NodeId.Should().Be(audit.NodeId);
        sales.AssociatedSuggestion!.ObjectIdentity!.Schema.Should().Be("sales");
        audit.AssociatedSuggestion!.ObjectIdentity!.Schema.Should().Be("audit");
        sales.SourceLocation!.Operator.Should().Be(sales.Identity);
        new PlanGraphConnectionUiActionService().UpdateHighlights(sales.SelectionKey, graph.MasterConnections);
        graph.MasterConnections.Where(c => c.IsHighlighted).Should().ContainSingle().Which.Source.Should().BeSameAs(sales);
        graph.MasterConnections.Should().OnlyContain(c => c.Source!.Identity!.QueryPlan == c.Target!.Identity!.QueryPlan);
    }

    [Theory]
    [InlineData("Database")]
    [InlineData("Schema")]
    [InlineData("Object")]
    [InlineData("Server")]
    [InlineData("QueryPlan")]
    [InlineData("Unknown")]
    public void MissingIndexAssociation_RequiresExactScopedIdentity(string mismatch)
    {
        var model = PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Fixture))!;
        var op = model.Operators[1];
        var identity = op.Objects.Single().Identity;
        var suggestion = new MissingIndexSuggestion { ObjectIdentity = identity, Location = op.Location };
        var node = new PlanGraphMissingIndexNodeInfo("T") { ObjectIdentity = identity, QueryPlan = op.Key.QueryPlan };
        var service = new PlanGraphMissingIndexAssociationService();
        service.MatchSuggestions([node], [suggestion]).Single().Should().BeSameAs(suggestion);
        var other = mismatch switch
        {
            "Database" => identity with { Database = "dbB" },
            "Schema" => identity with { Schema = "audit" },
            "Object" => identity with { Object = "t" },
            "Server" => identity with { Server = "server" },
            "Unknown" => identity with { Database = null },
            _ => identity
        };
        var changed = node with { ObjectIdentity = other,
            QueryPlan = mismatch == "QueryPlan" ? model.QueryPlans[1].Key : node.QueryPlan };
        service.MatchSuggestions([changed], [suggestion]).Single().Should().BeNull();
        if (mismatch == "Unknown")
        {
            suggestion.ObjectIdentity = other;
            service.MatchSuggestions([changed], [suggestion]).Single().Should().BeNull();
        }
    }

    [Fact]
    public void MissingIndexAssociation_AmbiguousCandidatesAreNotReducedToFirstMatch()
    {
        var model = PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Fixture))!;
        var op = model.Operators[1];
        var suggestion = new MissingIndexSuggestion { ObjectIdentity = op.Objects.Single().Identity, Location = op.Location };
        var node = new PlanGraphMissingIndexNodeInfo("T") { ObjectIdentity = suggestion.ObjectIdentity, QueryPlan = op.Key.QueryPlan };
        new PlanGraphMissingIndexAssociationService().MatchSuggestions([node], [suggestion, suggestion]).Single().Should().BeNull();
    }

    [Fact]
    public void Comparison_UsesSelectedQueryPlansAndRejectsMissingOrStaleSelectionWithoutDump()
    {
        var input = new InputRecognitionService().Parse(PlanIdentityModelTests.Fixture);
        var snapshot = new TuningSessionService().CaptureSnapshot(input.Document!, "plan.sqlplan", 1);
        snapshot.IdentityModel!.Operators.Select(op => op.Key).Should()
            .Equal(PlanIdentityAdapter.GetDocument(input.Document)!.Operators.Select(op => op.Key));
        snapshot.IdentityModel.Envelope.Should().BeSameAs(input.Envelope);
        snapshot.StatementText.Should().Contain("sales.T").And.Contain("audit.T").And.Contain("OPEN c");
        var reporter = new Reporter();
        var controller = new PlanComparisonController(unexpectedErrors: reporter);
        var allStatements = controller.BuildComparison(snapshot, null, Ns);
        allStatements.Statements.SelectMany(s => s.RootsA).Should().HaveCount(snapshot.IdentityModel.QueryPlans.Count);
        var selected = snapshot.IdentityModel.QueryPlans[3];
        snapshot.SelectedQueryPlan = selected.Key;
        var comparison = controller.BuildComparison(snapshot, null, Ns);
        comparison.PlanA!.Identity.Should().Be(selected.Operators.Single().Key);
        comparison.PlanA.Source.Descendants(Ns + "Object").Single().Attribute("Alias")!.Value.Should().Be("[other]");
        snapshot.Document.Root!.SetAttributeValue("Build", "changed");
        Action stale = () => controller.BuildComparison(snapshot, null, Ns);
        stale.Should().Throw<InvalidDataException>().WithMessage("PLAN_SELECTION_NOT_FOUND*");
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void MultiStatementAnalysis_ShowsEveryStatementAndDoesNotRunAutomaticRefactoring()
    {
        var refactoring = new RefactoringSpy();
        var files = new PhysicalFileHandler();
        using var temporary = new TemporaryFileManager(_directory);
        var orchestrator = new ApplicationOrchestrator(new SqlXmlAnalysisEngine(), refactoring, files,
            new ConsoleResultReporter(), NullLogger<ApplicationOrchestrator>.Instance);
        var result = new PlanAnalysisService(orchestrator, files, temporary)
            .Analyze(SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Fixture), Ns, "unused.sqlplan");
        result.QueryText.Should().Contain("sales.T").And.Contain("audit.T").And.Contain("USE dbB").And.Contain("OPEN c");
        result.WarningsText.Should().Contain("需要明确选定单条语句");
        result.Plan!.Statements.Should().HaveCount(4);
        refactoring.Calls.Should().Be(0);
        Directory.GetFiles(_directory).Should().BeEmpty();
    }

    [Fact]
    public void Visualizations_IncludeEveryQueryPlanAndDistinctOperatorSourceKeys()
    {
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Fixture);
        var model = PlanIdentityAdapter.GetDocument(document)!;
        string mermaid = ExecutionPlanVisualizer.GenerateMermaidPlan(document, Ns);
        mermaid.Split('\n').Count(line => line.Contains("subgraph qp", StringComparison.Ordinal)).Should().Be(4);
        mermaid.Split('\n').Count(line => line.Contains("%% Source:", StringComparison.Ordinal)).Should().Be(6);
        string tree = ExecutionPlanVisualizer.GenerateTextTree(document, Ns);
        foreach (var op in model.Operators)
        {
            mermaid.Should().Contain($"%% Source: {op.Key}");
            tree.Should().Contain(op.Key.ToString());
        }
    }

    [Fact]
    public void ExtensionOperators_ArePreservedAsSourceButExcludedFromAdapters()
    {
        string xml = PlanIdentityModelTests.Wrap("<StmtSimple><QueryPlan><RelOp NodeId='0'><TableScan/>" +
            "<InternalInfo><RelOp NodeId='fake' PhysicalOp='Table Scan'/></InternalInfo></RelOp>" +
            "<InternalInfo><QueryPlan><RelOp NodeId='also-fake'/></QueryPlan></InternalInfo></QueryPlan></StmtSimple>");
        var document = SafeXmlHelper.ParseSafe(xml);
        var rule = new RuleEngine();
        rule.RegisterRule(new ScopeRule(RuleScope.Operator));
        rule.AnalyzePlan(document, Ns).Should().ContainSingle().Which.NodeId.Should().Be("0");
        LoadGraph(document).MasterNodes.Should().ContainSingle().Which.NodeId.Should().Be("0");
        ExecutionPlanVisualizer.GenerateMermaidPlan(document, Ns).Should().NotContain("also-fake").And.NotContain("Nfake");
        document.Descendants(Ns + "RelOp").Should().HaveCount(3, "extension XML is preserved");
    }

    [Fact]
    public void CliReadAndScan_ExposeFullHierarchyObjectAndIssueOwnership()
    {
        string path = WriteFixture();
        var read = DocumentReadContractTests.RunCli("read", path);
        read.Code.Should().Be(0);
        using var readJson = JsonDocument.Parse(read.Output);
        readJson.RootElement.GetProperty("Privacy").GetString().Should().NotBeNullOrEmpty();
        var readPlan = readJson.RootElement.GetProperty("Plan");
        readPlan.GetProperty("Batches").GetArrayLength().Should().Be(2);
        var scan = DocumentReadContractTests.RunCli("--path", path, "--format", "json");
        using var scanJson = JsonDocument.Parse(scan.Output);
        var result = scanJson.RootElement[0];
        result.GetProperty("InputStatus").GetString().Should().Be("Success");
        result.GetProperty("Plan").GetProperty("Envelope").GetProperty("SourceHash").GetString().Should()
            .Be(readPlan.GetProperty("Envelope").GetProperty("SourceHash").GetString());
        var scans = result.GetProperty("ScannedOperators").EnumerateArray().ToList();
        scans.Should().HaveCount(4);
        scans.Select(op => op.GetProperty("Location").GetProperty("DisplayScope").GetString()).Should().OnlyHaveUniqueItems();
        scans[0].GetProperty("Objects")[0].GetProperty("Identity").GetProperty("Schema").GetString().Should().Be("sales");
        scans[1].GetProperty("Objects")[0].GetProperty("Identity").GetProperty("Schema").GetString().Should().Be("audit");
        result.GetProperty("Issues").EnumerateArray().Should().OnlyContain(issue => issue.GetProperty("Location").ValueKind == JsonValueKind.Object);
    }

    private static PlanGraphLoadUiActionResult LoadGraph(XDocument document) => new PlanGraphLoadUiActionService().Load(
        document, Ns, new ObservableCollection<PlanNodeViewModel>(), new ObservableCollection<ConnectionViewModel>(),
        new PlanGraphLoadUiActionOptions { InitialLayout = PlanLayoutMode.Horizontal, InitialColor = PlanColorMode.TotalCost,
            InitialView = DiagramViewMode.Rows, InitialLinkMetric = LinkMetricMode.RowCount, ResidualIoThreshold = 10, ResidualIoMinRowsRead = 1000 });
    private string WriteFixture()
    {
        string path = Path.Combine(_directory, "identities.sqlplan");
        File.WriteAllText(path, PlanIdentityModelTests.Fixture);
        return path;
    }
    private sealed class ScopeRule(RuleScope scope) : IPlanAnalyzerRule
    {
        public string RuleId => "TEST_IMP10_" + scope;
        public string Name => RuleId;
        public string Description => "Identity propagation test";
        public RuleMetadata Metadata => new(RuleId, RuleCategory.AntiPattern, scope, "Warning", Description);
        public AnalysisResult Analyze(XElement relOp, XNamespace ns) => new() { RuleId = RuleId, NodeId = (string?)relOp.Attribute("NodeId") ?? "" };
    }
    private sealed class Reporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; return new(null, null, "test"); }
    }
    private sealed class RefactoringSpy : IRefactoringEngine
    {
        public int Calls { get; private set; }
        public RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun)
        { Calls++; throw new InvalidOperationException("Unexpected automatic rewrite"); }
    }
    public void Dispose()
    {
        string path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp10Flow-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup target");
        Directory.Delete(path, recursive: true);
    }
}
