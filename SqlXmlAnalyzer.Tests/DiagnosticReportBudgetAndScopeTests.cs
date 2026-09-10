using System.IO;
using System.Xml.Linq;
using System.Xml.Schema;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Privacy;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class DiagnosticReportBudgetAndScopeTests
{
    internal static XDocument Fixture(string suffix)
    {
        var assembly = typeof(DiagnosticReportBudgetAndScopeTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!;
        using var reader = new StreamReader(stream);
        return SafeXmlHelper.ParseSafe(reader.ReadToEnd());
    }

    internal static void AssertValidSchema(XDocument document)
    {
        using var reader = Fixture("showplanxml-sql2019.xsd").CreateReader();
        var schemas = new XmlSchemaSet { XmlResolver = null };
        schemas.Add(XmlSchema.Read(reader, null)!);
        var errors = new List<string>();
        document.Validate(schemas, (_, e) => errors.Add(e.Message));
        errors.Should().BeEmpty();
    }

    internal static (PlanDocument Model, PlanDiagnosticReport Diagnostics) Analyze(XDocument document)
    {
        var model = PlanIdentityAdapter.GetDocument(document)!;
        var diagnostics = new RuleEngine().AnalyzePlanDetailed(document, document.Root!.Name.Namespace, model);
        return (model, diagnostics);
    }

    [Theory]
    [InlineData(0, "PopulateQuery", false)]
    [InlineData(1, "FetchQuery", false)]
    [InlineData(0, "PopulateQuery", true)]
    [InlineData(1, "FetchQuery", true)]
    public void SelectedCursor_PreservesAncestorsOptionsAndOnlySelectedOperation(int index, string operation, bool redact)
    {
        var source = Fixture("imp22_cursor_export.sqlplan");
        string before = source.ToString();
        AssertValidSchema(source);
        var (model, diagnostics) = Analyze(source);
        var report = DiagnosticReportFactory.Plan(source, model, diagnostics, model.Statements[0].Key, model.QueryPlans[index].Key);
        if (redact)
        {
            var preview = new ReportRedactionService().Preview(report);
            preview.CanExport.Should().BeTrue(preview.Summary);
            report = preview.Report!;
        }
        var result = SafeXmlHelper.ParseSafe(report.SourceXml);
        AssertValidSchema(result);
        XNamespace ns = source.Root!.Name.Namespace;
        result.Descendants(ns + "Operation").Should().ContainSingle().Which.Attribute("OperationType")!.Value.Should().Be(operation);
        result.Descendants(ns + "StatementSetOptions").Should().ContainSingle();
        if (!redact) result.Descendants(ns + "CursorPlan").Single().Attribute("CursorName")!.Value.Should().Be("c");
        source.ToString().Should().Be(before);
    }

    internal static XDocument Operators(int count, bool metrics = false) => SafeXmlHelper.ParseSafe(
        PlanIdentityModelTests.Wrap("<StmtSimple><QueryPlan>" + string.Concat(Enumerable.Range(0, count)
            .Select(i => $"<RelOp NodeId='{i}'{(metrics ? " EstimateRows='1'" : "")}/>")) + "</QueryPlan></StmtSimple>"));

    [Fact]
    public void EmptyOperators_KeepMissingStateWithoutExpandingEveryAbsentMetric()
    {
        var source = Operators(1000);
        var (model, diagnostics) = Analyze(source);
        var report = DiagnosticReportFactory.Plan(source, model, diagnostics);
        report.FactCount.Should().BeLessThan(10_000);
        DiagnosticReportRenderer.Text(report).Length.Should().BeLessThan(1_000_000);
        report.Facts.Should().Contain(f => f.Name == "B1/S1/Q1/O1/Metrics/State" && f.Value == "Missing");
        report.Facts.Should().Contain(f => f.Name == "B1/S1/Q1/O1/Metrics/Value" && f.Value == "N/A");
        report.Nodes.Should().HaveCount(1000);
    }

    [Fact]
    public void SelectedCursorStatement_WithoutQueryPlanFilterKeepsBothOperations()
    {
        var source = Fixture("imp22_cursor_export.sqlplan");
        var (model, diagnostics) = Analyze(source);
        var report = DiagnosticReportFactory.Plan(source, model, diagnostics, model.Statements[0].Key);
        var result = SafeXmlHelper.ParseSafe(report.SourceXml);
        AssertValidSchema(result);
        result.Descendants(source.Root!.Name.Namespace + "Operation").Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CursorSqlplan_ExportedFileRemainsSchemaValid(bool redact)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-CursorExport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "selected.sqlplan");
        try
        {
            var source = Fixture("imp22_cursor_export.sqlplan"); var (model, diagnostics) = Analyze(source);
            var report = DiagnosticReportFactory.Plan(source, model, diagnostics, model.Statements[0].Key, model.QueryPlans[1].Key);
            if (redact) report = new ReportRedactionService().Preview(report).Report!;
            var result = new DiagnosticReportExportService().Export(report, "sqlplan", path);
            result.Succeeded.Should().BeTrue(result.Message);
            AssertValidSchema(SafeXmlHelper.ParseSafe(File.ReadAllText(path)));
            File.ReadAllText(path).Should().Be(report.SourceXml);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    [Fact]
    public void PrefixedCursor_PreservesNamespacesWhenProjectingTheOriginalTree()
    {
        var source = Fixture("imp22_cursor_export.sqlplan");
        source.Root!.Attribute("xmlns")!.Remove();
        source.Root.Add(new XAttribute(XNamespace.Xmlns + "sp", source.Root.Name.NamespaceName));
        AssertValidSchema(source);
        var (model, diagnostics) = Analyze(source);
        var report = DiagnosticReportFactory.Plan(source, model, diagnostics, model.Statements[0].Key, model.QueryPlans[0].Key);
        AssertValidSchema(SafeXmlHelper.ParseSafe(report.SourceXml));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimpleAndConditionalPlans_KeepStatementOptionsAndRequiredContainers(bool conditional)
    {
        var source = Fixture("imp22_cursor_export.sqlplan");
        XNamespace ns = source.Root!.Name.Namespace;
        var cursor = source.Descendants(ns + "StmtCursor").Single();
        var query = new XElement(source.Descendants(ns + "QueryPlan").First());
        var options = new XElement(cursor.Element(ns + "StatementSetOptions")!);
        cursor.ReplaceWith(new XElement(ns + (conditional ? "StmtCond" : "StmtSimple"), new XAttribute("StatementText", "outer"), options,
            conditional ? new XElement(ns + "Condition", query) : query,
            conditional ? new XElement(ns + "Then", new XElement(ns + "Statements", new XElement(ns + "StmtSimple", new XAttribute("StatementText", "inner")))) : null));
        AssertValidSchema(source);
        var (model, diagnostics) = Analyze(source);
        var report = DiagnosticReportFactory.Plan(source, model, diagnostics, model.Statements[0].Key, model.QueryPlans[0].Key);
        var result = SafeXmlHelper.ParseSafe(report.SourceXml);
        AssertValidSchema(result);
        XNode.DeepEquals(result.Descendants(ns + "StatementSetOptions").Single(), options).Should().BeTrue();
        report.SourceXml.Should().NotContain("inner");
        if (conditional) result.Descendants(ns + "Then").Single().Element(ns + "Statements").Should().NotBeNull();
    }

    [Fact]
    public void ReceiveScope_RejectsSingleOperationButAllowsCompleteStatement()
    {
        var source = Fixture("imp22_cursor_export.sqlplan");
        XNamespace ns = source.Root!.Name.Namespace;
        source.Descendants(ns + "StmtCursor").Single().Name = ns + "StmtReceive";
        var plan = source.Descendants(ns + "CursorPlan").Single();
        plan.Name = ns + "ReceivePlan"; plan.RemoveAttributes();
        var operations = plan.Elements().ToArray();
        operations[0].SetAttributeValue("OperationType", "ReceivePlanSelect");
        operations[1].SetAttributeValue("OperationType", "ReceivePlanUpdate");
        AssertValidSchema(source);
        var (model, diagnostics) = Analyze(source);
        Action selected = () => DiagnosticReportFactory.Plan(source, model, diagnostics, model.Statements[0].Key, model.QueryPlans[0].Key);
        selected.Should().Throw<InvalidDataException>().WithMessage("REPORT_SCOPE_NOT_REPRESENTABLE*");
        var whole = DiagnosticReportFactory.Plan(source, model, diagnostics, model.Statements[0].Key);
        AssertValidSchema(SafeXmlHelper.ParseSafe(whole.SourceXml));
    }

    [Fact]
    public void PresentMetrics_RejectOversizedProjectionBeforeRetainingFullReport()
    {
        var source = Operators(1000, metrics: true);
        var (model, diagnostics) = Analyze(source);
        Action create = () => DiagnosticReportFactory.Plan(source, model, diagnostics);
        create.Should().Throw<ReportBudgetExceededException>().WithMessage("REPORT_BUDGET_EXCEEDED*");
    }

    [Fact]
    public void SourceXml_ConsumesTheSameBudgetAsFacts()
    {
        var source = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap("<StmtSimple StatementText='" + new string('x', 4_100_000) + "'/>"));
        var (model, diagnostics) = Analyze(source);
        Action create = () => DiagnosticReportFactory.Plan(source, model, diagnostics, model.Statements[0].Key);
        create.Should().Throw<ReportBudgetExceededException>();
        source.Descendants().Single(e => e.Name.LocalName == "StmtSimple").Attribute("StatementText")!.Value.Length.Should().Be(4_100_000);
    }

    [Fact]
    public void SnapshotBudget_StopsLazyEnumerationBeforeMaterializingAllRecords()
    {
        int enumerated = 0;
        IEnumerable<ReportField> Fields()
        {
            for (int i = 0; i < 1_000_000; i++) { enumerated++; yield return new("", ""); }
        }
        Action create = () => new DiagnosticReport("ExecutionPlan", "Document", [], Fields(), [], [], [], [], "<x/>");
        create.Should().Throw<ReportBudgetExceededException>();
        enumerated.Should().Be(DiagnosticReportLimits.MaxRecords + 1);
    }

    [Fact]
    public void SnapshotBudget_ChecksOversizedXmlBeforeEnumeratingFields()
    {
        bool enumerated = false;
        IEnumerable<ReportField> Fields() { enumerated = true; yield return new("x", "y"); }
        Action create = () => new DiagnosticReport("ExecutionPlan", "Document", [], Fields(), [], [], [], [], new string('x', DiagnosticReportLimits.MaxContentCharacters));
        create.Should().Throw<ReportBudgetExceededException>();
        enumerated.Should().BeFalse();
    }

    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    [InlineData("html")]
    [InlineData("svg")]
    public void Renderers_ObserveCancellationBeforeProducingOutput(string format)
    {
        var report = DiagnosticReportTests.Report(); var token = new CancellationToken(true);
        Action render = () => _ = format switch
        {
            "text" => DiagnosticReportRenderer.Text(report, token), "json" => DiagnosticReportRenderer.Json(report, token),
            "html" => DiagnosticReportRenderer.Html(report, token), _ => DiagnosticReportRenderer.Svg(report, token)
        };
        render.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Factory_ObservesCancellationBeforeProjectingFacts()
    {
        var source = Operators(1); var (model, diagnostics) = Analyze(source);
        Action create = () => DiagnosticReportFactory.Plan(source, model, diagnostics, token: new CancellationToken(true));
        create.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void TextWriter_RejectsOverflowBeforeAppendingAnyPartOfTheWrite()
    {
        using var writer = new ReportTextWriter(64);
        for (int i = 0; i < 63; i++) writer.Write('x');
        Action overflow = () => writer.Write("yz");
        overflow.Should().Throw<ReportBudgetExceededException>();
        writer.ToString().Should().Be(new string('x', 63));
        writer.Write('z'); writer.ToString().Length.Should().Be(64);
    }

    [Fact]
    public void JsonBuffer_RejectsOversizedHintBeforeAllocating()
    {
        var buffer = new ReportByteBuffer(32);
        buffer.GetMemory(4).Span[..4].Fill(42); buffer.Advance(4);
        Action overflow = () => buffer.GetMemory(100_000_000);
        overflow.Should().Throw<ReportBudgetExceededException>();
        buffer.WrittenMemory.ToArray().Should().Equal(42, 42, 42, 42);
    }

    [Fact]
    public void Preview_CachesEachImmutablePrivacySnapshot()
    {
        var vm = new ReportReviewViewModel(DiagnosticReportTests.Report());
        string redacted = vm.PreviewText;
        vm.PreviewText.Should().BeSameAs(redacted);
        vm.IsRedacted = false; string raw = vm.PreviewText;
        vm.PreviewText.Should().BeSameAs(raw); raw.Should().NotBe(redacted);
        vm.IsRedacted = true; vm.PreviewText.Should().BeSameAs(redacted);
    }
}
