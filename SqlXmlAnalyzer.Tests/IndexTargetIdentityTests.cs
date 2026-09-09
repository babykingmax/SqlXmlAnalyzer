using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class IndexTargetIdentityTests
{
    internal static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;
    internal static XDocument Fixture() => SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp15_index_targets.sqlplan"));

    [Fact]
    public void Extraction_RetainsRolesOrderAndFullIdentityForEveryStatement()
    {
        var suggestions = PlanDiagnosticAnalyzer.ExtractMissingIndexes(Fixture(), Ns);
        suggestions.Should().HaveCount(4);
        suggestions.Select(s => s.Location!.Statement).Should().OnlyHaveUniqueItems();
        suggestions.Select(s => s.ObjectIdentity!.Database).Should().Equal("db.one", "db.one", "db.two", "db.one");
        suggestions.Select(s => s.ObjectIdentity!.Schema).Should().Equal("sales", "audit", "sales", "sales");
        suggestions[0].KeyColumns.Select(c => c.Usage).Should().Equal("EQUALITY", "INEQUALITY");
        suggestions[0].KeyColumns.Select(c => c.Name).Should().Equal("[K]", "[R]]ange]");
        suggestions[0].IncludeColumns.Single().Name.Should().Be("[V]");
        suggestions.Select(s => s.CreateIndexStatement).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Extraction_IgnoresMisplacedOpaqueAndForeignMissingIndexes()
    {
        var doc = Fixture();
        var index = new XElement(doc.Descendants(Ns + "MissingIndexes").First());
        doc.Root!.Add(new XElement(Ns + "InternalInfo", index));
        doc.Descendants(Ns + "RelOp").First().Add(new XElement(index));
        doc.Root.Add(new XElement(XName.Get("Other", "urn:foreign"), new XElement(index)));
        PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, Ns).Should().HaveCount(4);
    }

    [Fact]
    public void AssociationAndSandbox_UseOnlyTheExactObjectInTheExactQueryPlan()
    {
        var doc = Fixture();
        var suggestions = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, Ns);
        var model = PlanIdentityAdapter.GetDocument(doc)!;
        foreach (var suggestion in suggestions)
        {
            var op = IndexTargetResolver.FindOperators(suggestion, doc, Ns).Should().ContainSingle().Subject;
            model.FindOperator(op)!.Objects.Single().Identity.Should().Be(suggestion.ObjectIdentity);
            model.FindLocation(op)!.QueryPlan.Should().Be(suggestion.Location!.QueryPlan);
        }
        var vm = new IndexSandboxViewModel(suggestions[0], doc);
        vm.CreateIndexStatement.Should().Contain("ON [db.one].[sales].[Order.Detail]");
        vm.TotalRows.Should().Be(10000);
        vm.AvgRowSize.Should().Be(32);
        vm.ReturnedRows.Should().Be(100);
        vm.AvailableColumns.Should().NotContain(c => c.Contains("Only"));
        vm.IsCoveredIndex.Should().BeTrue();
        suggestions[0].Location = suggestions[1].Location;
        IndexTargetResolver.FindOperators(suggestions[0], doc, Ns).Should().BeEmpty();
    }

    [Fact]
    public void NativeRule_ReportsAllStatementsWithRepeatedNodeZeroAndRetainsJsonTargets()
    {
        var doc = Fixture();
        var engine = new RuleEngine();
        engine.RegisterRule(new SargableIndexRecommendationRule());
        var report = engine.AnalyzePlanDetailed(doc, Ns);
        report.HasFailures.Should().BeFalse(string.Join("; ", report.Runs.Select(r => r.Reason)));
        report.Runs.Should().HaveCount(4).And.OnlyContain(r => r.Status == RuleRunStatus.Hit && r.NodeId == null);
        report.Diagnostics.Should().HaveCount(4).And.OnlyContain(d => d.SemanticCode == "INDEX_SQL_CANDIDATE");
        report.Diagnostics.Select(d => d.Location.Statement).Should().OnlyHaveUniqueItems();
        report.Diagnostics[0].Summary.Should().Contain("ON [db.one].[sales].[Order.Detail]")
            .And.NotContain("AuditOnly").And.NotContain("OtherStatementOnly");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report));
        json.RootElement.GetProperty("Diagnostics").GetArrayLength().Should().Be(4);
        foreach (var op in doc.Descendants(Ns + "RelOp"))
            engine.AnalyzeNodeDetailed(op, Ns).Diagnostics.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Database")]
    [InlineData("Schema")]
    public void SqlBinding_MissingPlanIdentityDoesNotGenerateDdl(string missing)
    {
        var doc = Fixture();
        doc.Descendants(Ns + "Object").First().Attribute(missing)!.Remove();
        var engine = new RuleEngine();
        engine.RegisterRule(new SargableIndexRecommendationRule());
        var diagnostic = engine.AnalyzePlanDetailed(doc, Ns).Diagnostics[0];
        diagnostic.SemanticCode.Should().Be("INDEX_TARGET_UNRESOLVED");
        diagnostic.Summary.Should().NotContain("CREATE");
    }

    [Fact]
    public void SqlBinding_ChangedSourceCannotReuseOldTargetOrStatementLocation()
    {
        var doc = Fixture();
        var rule = new SargableIndexRecommendationRule();
        var op = doc.Descendants(Ns + "RelOp").First();
        rule.Analyze(op, Ns)!.Message.Should().Contain("ON [db.one].[sales].[Order.Detail]");
        op.Descendants(Ns + "Object").Single().SetAttributeValue("Schema", "[audit]");
        rule.Analyze(op, Ns).Should().BeNull();
    }

    [Fact]
    public void SqlSuggestions_KeepQualifiedTargetsAndDoNotGuessAmbiguousColumns()
    {
        var suggestions = MissingIndexSuggester.SuggestIndexes("SELECT s.V, a.AuditOnly FROM [db.one].sales.T s JOIN [db.one].audit.T a ON s.K=a.A WHERE s.K=1");
        suggestions.Should().HaveCount(2);
        suggestions.Select(s => s.ObjectIdentity!.Schema).Should().BeEquivalentTo("sales", "audit");
        suggestions.Single(s => s.ObjectIdentity!.Schema == "sales").KeyColumns.Select(c => c.Name).Should().Equal("[K]");
        suggestions.Single(s => s.ObjectIdentity!.Schema == "audit").KeyColumns.Select(c => c.Name).Should().Equal("[A]");
        MissingIndexSuggester.SuggestIndexes("SELECT 1 FROM sales.T s CROSS JOIN audit.T a WHERE K=1").Should().BeEmpty();
        MissingIndexSuggester.SuggestIndexes("SELECT 1 FROM sales.T s WHERE unknown.K=1").Should().BeEmpty();
    }

    [Fact]
    public void SqlSuggestions_PreserveQuotedColumnNamesAndDoNotInventDefaultSchemaOrCteTable()
    {
        var suggestion = MissingIndexSuggester.SuggestIndexes("SELECT [V]]x] FROM [db.one].[sales].[Order.Detail] WHERE [K]]x]=1").Single();
        suggestion.KeyColumns.Single().Name.Should().Be("[K]]x]");
        suggestion.CreateIndexStatement.Should().Contain("([K]]x])").And.Contain("INCLUDE ([V]]x])");
        MissingIndexSuggester.SuggestIndexes("SELECT V FROM T WHERE K=1").Single().ObjectIdentity!.Schema.Should().BeNull();
        MissingIndexSuggester.SuggestIndexes("WITH C AS (SELECT K FROM sales.T) SELECT K FROM C WHERE K=1")
            .Should().NotContain(s => s.Table == "C");
    }

    [Theory]
    [InlineData("SELECT 1 FROM sales.T t CROSS JOIN (SELECT 1 AS K) d WHERE K=1")]
    [InlineData("WITH C AS (SELECT 1 AS K) SELECT 1 FROM sales.T t CROSS JOIN C WHERE K=1")]
    [InlineData("SELECT t.V FROM sales.T t WHERE EXISTS(SELECT 1 FROM (SELECT 1 AS K) t WHERE t.K=1)")]
    public void SqlSuggestions_UnresolvedSourcesAndShadowedAliasesCannotBorrowKnownTable(string sql)
    {
        MissingIndexSuggester.SuggestIndexes(sql).Should().BeEmpty();
    }
}
