using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.Analysis;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Compatibility;

public sealed class ModelReportCompatibilityTests
{
    // rules-before.json is the historical IMP-28 capture and must remain unchanged.
    // These nine reviewed correctness fixes intentionally change diagnostic semantics:
    // six rules now run per QueryPlan; the other three fix predicate ownership,
    // SELECT-list AST counting, and spill-only classification respectively.
    private static readonly IReadOnlyDictionary<string, (string From, string To, RuleScope Scope)> ReviewedRuleMigrations =
        new Dictionary<string, (string, string, RuleScope)>(StringComparer.Ordinal)
        {
            ["RULE_003_PARAM_SNIFFING"] = ("1.0.0", "2.0.0", RuleScope.QueryPlan),
            ["RULE_013_ANTI_PATTERN"] = ("1.0.0", "2.0.0", RuleScope.Operator),
            ["RULE_014_SERIAL_PLAN_REASON"] = ("1.0.0", "2.0.0", RuleScope.QueryPlan),
            ["RULE_017_LARGE_MEMORY_GRANT"] = ("1.0.0", "2.0.0", RuleScope.QueryPlan),
            ["RULE_019_CACHE_RECOMPILE"] = ("1.0.0", "2.0.0", RuleScope.QueryPlan),
            ["RULE_024_SCALAR_SUBQUERY_PATTERN"] = ("1.0.0", "2.0.0", RuleScope.Statement),
            ["RULE_028_STATS_USAGE"] = ("1.0.0", "2.0.0", RuleScope.QueryPlan),
            ["RULE_029_MEMORY_GRANT_DOC"] = ("1.0.0", "2.0.0", RuleScope.QueryPlan),
            ["RULE_032_MEMORY_SPILL"] = ("1.0.0", "2.0.0", RuleScope.Operator)
        };

    [Fact]
    public void Plan_ExistingAndStructuredAdaptersPreserveRuleIdentitySeverityAndScope()
    {
        var document = SafeXmlHelper.ParseSafe(CompatibilityFixtures.Read("plan.sqlplan"));
        var recognized = InputRecognitionService.Recognize(document);
        var model = PlanIdentityAdapter.GetDocument(document)!;
        var structured = PlanDiagnosticAnalyzer.AnalyzeDetailed(document, document.Root!.Name.Namespace);
        var legacy = PlanDiagnosticAnalyzer.AnalyzePlan(document, document.Root.Name.Namespace);
        legacy.Select(r => (r.RuleId, r.Severity, r.NodeId, r.Location?.DisplayScope))
            .Should().Equal(structured.ToLegacyResults().Select(r => (r.RuleId, r.Severity, r.NodeId, r.Location?.DisplayScope)));
        var application = SqlXmlAnalysisEngine.FromDiagnostics(recognized, model, structured);
        application.IsSuccess.Should().BeTrue();
        application.Issues.Select(i => (i.IssueType, i.Location?.DisplayScope))
            .Should().Equal(legacy.Select(r => (r.RuleId, r.Location?.DisplayScope)));
        model.Statements.Should().ContainSingle();
        model.Operators.Should().ContainSingle();
        model.GetOperatorSource(model.Operators[0].Key)!.Attribute("NodeId")!.Value.Should().Be("0");
    }

    [Fact]
    public void DefaultRules_PreserveFrozenIdsAndSeveritiesWithExplicitVersionMigrations()
    {
        using var baseline = JsonDocument.Parse(CompatibilityFixtures.Read("rules-before.json"));
        var engine = new RuleEngine(configuration: Core.Configuration.RuleConfigurationDocument.Defaults);
        engine.RegisterDefaultRules();
        var frozen = baseline.RootElement.EnumerateArray().Select(r =>
            (RuleId: r.GetProperty("RuleId").GetString()!, Version: r.GetProperty("Version").GetString()!,
                DefaultSeverity: r.GetProperty("DefaultSeverity").GetString()!)).ToArray();
        var expected = frozen.Select(rule => (rule.RuleId,
            Version: ReviewedRuleMigrations.TryGetValue(rule.RuleId, out var migration) ? migration.To : rule.Version,
            rule.DefaultSeverity)).ToArray();
        engine.RegisteredRules.Select(r => (r.RuleId, r.Metadata.Version, r.Metadata.DefaultSeverity))
            .Should().BeEquivalentTo(expected);
        expected.Should().HaveCount(34);
        foreach (var (ruleId, migration) in ReviewedRuleMigrations)
        {
            frozen.Single(rule => rule.RuleId == ruleId).Version.Should().Be(migration.From);
            engine.RegisteredRules.Single(rule => rule.RuleId == ruleId).Metadata.Scope.Should().Be(migration.Scope);
        }
        engine.RegisteredRules.Where(rule => rule.Metadata.Version != frozen.Single(old => old.RuleId == rule.RuleId).Version)
            .Select(rule => rule.RuleId).Order(StringComparer.Ordinal)
            .Should().Equal(ReviewedRuleMigrations.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Report_CurrentJsonRetainsFrozenReaderFieldsAndRecognizableVersion()
    {
        var document = SafeXmlHelper.ParseSafe(CompatibilityFixtures.Read("plan.sqlplan"));
        var report = DiagnosticReportTests.Report(document);
        string output = DiagnosticReportRenderer.Json(report);
        using var baseline = JsonDocument.Parse(CompatibilityFixtures.Read("report-imp22-before.json"));
        using var actual = JsonDocument.Parse(output);
        foreach (var property in baseline.RootElement.EnumerateObject())
        {
            actual.RootElement.TryGetProperty(property.Name, out var value).Should().BeTrue(property.Name);
            value.ValueKind.Should().Be(property.Value.ValueKind, property.Name);
        }
        var consumer = JsonSerializer.Deserialize<ReportConsumer>(output)!;
        consumer.SchemaVersion.Should().Be("IMP22-1.0");
        consumer.Kind.Should().Be("ExecutionPlan");
        consumer.Scope.Should().Be("Document");
        consumer.SourceXml.Should().Contain("CompatibilityFixture");
        consumer.Facts.Should().Contain(f => f.Name == "B1/S1/Q1/O1/OutputRows/State" && f.Value == "Available");
        DiagnosticReportRenderer.Text(report).Should().Contain("DiagnosticReport IMP22-1.0");
        DiagnosticReportRenderer.Html(report).Should().Contain("IMP22-1.0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deadlock_LegacyWrapperAndExplicitEventPreserveProcessesAndResources(bool wrapper)
    {
        string xml = CompatibilityFixtures.Read("deadlock.xdl");
        if (wrapper) xml = "<deadlock-list>" + xml + "</deadlock-list>";
        var document = SafeXmlHelper.ParseSafe(xml);
        var input = InputRecognitionService.Recognize(document);
        input.IsSuccess.Should().BeTrue();
        input.Deadlocks.Should().ContainSingle();
        var legacy = DeadlockXmlParser.TryParseDeadlockXml(document);
        var selected = DeadlockXmlParser.TryParseDeadlockXml(input.Deadlocks[0].Document);
        legacy.IsSuccess.Should().BeTrue();
        selected.IsSuccess.Should().BeTrue();
        legacy.Value!.Processes.Select(p => (p.Id, p.Spid, p.DeadlockPriority))
            .Should().Equal(selected.Value!.Processes.Select(p => (p.Id, p.Spid, p.DeadlockPriority)));
        legacy.Value.Resources.Count.Should().Be(2);
        selected.Value.Resources.Count.Should().Be(2);
    }

    private sealed class ReportConsumer
    {
        public string? SchemaVersion { get; set; }
        public string? Kind { get; set; }
        public string? Scope { get; set; }
        public string? SourceXml { get; set; }
        public ReportField[] Facts { get; set; } = [];
    }
}
