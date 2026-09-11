using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Rules;

public class CardinalityResidualRuleTests
{
    internal static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;
    internal static string Counter(string values) => $"<RunTimeCountersPerThread {values}/>";
    internal static XElement Op(string attributes = "EstimateRows='100'", string counters = "", string payload = "", string physical = "Index Seek") =>
        SafeXmlHelper.ParseSafe($"<RelOp xmlns='{Ns}' NodeId='4' PhysicalOp='{physical}' {attributes}>"
            + payload + (counters.Length == 0 ? "" : $"<RunTimeInformation>{counters}</RunTimeInformation>") + "</RelOp>").Root!;
    internal static string Predicate(string text = "[T].[V]=(2)") => new XElement("Predicate",
        new XElement("ScalarOperator", new XAttribute("ScalarString", text))).ToString(SaveOptions.DisableFormatting);
    internal static RuleEngine Engine(params IPlanAnalyzerRule[] rules)
    {
        var engine = new RuleEngine();
        foreach (var rule in rules) engine.RegisterRule(rule);
        return engine;
    }
    private static RuleEngine Cardinality() => Engine(new RowEstimateMismatchRule(), new CardinalityErrorRule());
    private static RuleEngine Residual() => Engine(new ResidualPredicateRule(), new ResidualPredOpRule());

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    public void Cardinality_SixteenWorkersUseCommonExecutionCount(bool reverse, int executions)
    {
        var counters = Enumerable.Range(1, 16).Select(i => Counter($"Thread='{i}' ActualRows='{1000 * executions}' ActualExecutions='{executions}'")).ToList();
        counters.Add(Counter("Thread='0' ActualRows='0' ActualExecutions='1'"));
        counters.Add(Counter("Thread='17' ActualRows='0' ActualExecutions='0'"));
        if (reverse) counters.Reverse();
        var op = Op("EstimateRows='16000'", string.Concat(counters));
        var report = Cardinality().AnalyzeNodeDetailed(op, Ns);
        report.Runs.Should().OnlyContain(r => r.Status == RuleRunStatus.NoHit);
        report.Diagnostics.Should().BeEmpty();
        new RowEstimateMismatchRule().Analyze(op, Ns).Should().BeNull();
        new CardinalityErrorRule().Analyze(op, Ns).Should().BeNull();
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(1, true)]
    public void Cardinality_NestedLoopRepeatedExecutionUsesPerExecutionRows(int executions, bool mismatch)
    {
        var op = Op("EstimateRows='100'", Counter($"Thread='0' ActualRows='10000' ActualExecutions='{executions}'"));
        var report = Cardinality().AnalyzeNodeDetailed(op, Ns);
        report.Diagnostics.Count.Should().Be(mismatch ? 1 : 0);
        if (mismatch)
        {
            var diagnostic = report.Diagnostics.Single();
            diagnostic.Origins.Should().HaveCount(2);
            diagnostic.Evidence.Single(e => e.Name == "RowsPerExecution").Value.Should().Be("10000");
            report.Runs.Should().OnlyContain(r => r.Status == RuleRunStatus.Hit);
        }
    }

    [Theory]
    [InlineData("1000", "100", "Critical", null)]
    [InlineData("100", "1000", "Warning", null)]
    [InlineData("100", "1100", "Warning", null)]
    [InlineData("100", "1101", "Warning", "Critical")]
    [InlineData("100", "10000", "Critical", "Critical")]
    [InlineData("1", "99", null, null)]
    [InlineData("0", "100", "Critical", null)]
    [InlineData("0.01", "100", "Critical", null)]
    [InlineData("5000", "0", "Critical", "Critical")]
    [InlineData("0", "0", null, null)]
    public void Cardinality_PreservesThresholdBoundariesAndZeroFloor(string estimate, string actual, string? rowSeverity, string? detailedSeverity)
    {
        var op = Op($"EstimateRows='{estimate}'", Counter($"Thread='0' ActualRows='{actual}' ActualExecutions='1'"));
        var row = new RowEstimateMismatchRule().Analyze(op, Ns);
        var detail = new CardinalityErrorRule().Analyze(op, Ns);
        (row?.Severity).Should().Be(rowSeverity);
        (detail?.Severity).Should().Be(detailedSeverity);
        var report = Cardinality().AnalyzeNodeDetailed(op, Ns);
        report.HasFailures.Should().BeFalse();
        if (report.Diagnostics.Count > 0)
        {
            var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
            diagnostic.SemanticCode.Should().Be(RowCountRuleEvaluator.CardinalityCode);
            diagnostic.Evidence.Should().Contain(e => e.Name == "ComparisonDenominator" && e.Value != null);
            diagnostic.Summary.Should().NotContain("根因").And.NotContain("过时");
            diagnostic.Hypotheses.Should().Contain(h => h.Contains("不能确定根因"));
        }
    }

    [Theory]
    [InlineData("EstimateRows='10'", "")]
    [InlineData("", "Thread='0' ActualRows='50000' ActualExecutions='1'")]
    [InlineData("EstimateRows='NaN'", "Thread='0' ActualRows='50000' ActualExecutions='1'")]
    [InlineData("EstimateRows='Infinity'", "Thread='0' ActualRows='50000' ActualExecutions='1'")]
    [InlineData("EstimateRows='-1'", "Thread='0' ActualRows='50000' ActualExecutions='1'")]
    [InlineData("EstimateRows='10'", "Thread='0' ActualRows='50000'")]
    [InlineData("EstimateRows='10'", "Thread='0' ActualRows='50000' ActualExecutions='0'")]
    [InlineData("EstimateRows='10'", "Thread='0' ActualRows='-1' ActualExecutions='1'")]
    public void Cardinality_MissingOrInvalidMeasurementsAreSkipped(string attributes, string counter)
    {
        var report = Cardinality().AnalyzeNodeDetailed(Op(attributes, counter.Length == 0 ? "" : Counter(counter)), Ns);
        report.Runs.Should().HaveCount(2).And.OnlyContain(r => r.Status == RuleRunStatus.Skipped && r.ReasonCode == "RULE_MISSING_EVIDENCE");
        report.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Thread='2' ActualRows='10000' ActualExecutions='2'")]
    [InlineData("Thread='1' ActualRows='10000' ActualExecutions='1'")]
    [InlineData("Thread='2' ActualRows='10000'")]
    [InlineData("Thread='0' ActualRows='10' ActualExecutions='1'")]
    public void Cardinality_AmbiguousWorkersNeverFabricateCommonDenominator(string second)
    {
        var op = Op("EstimateRows='100'", Counter("Thread='1' ActualRows='10000' ActualExecutions='1'") + Counter(second));
        Cardinality().AnalyzeNodeDetailed(op, Ns).Runs.Should().OnlyContain(r => r.Status == RuleRunStatus.Skipped);
    }

    [Fact]
    public void Cardinality_DeduplicationRetainsSupplementalPredicatesOriginsAndSeverity()
    {
        var op = Op("EstimateRows='100'", Counter("ActualRows='1101' ActualExecutions='1'"),
            "<IndexScan>" + Predicate("[A]=1 AND [B]=2") + "<SeekPredicates><ScalarOperator ScalarString='[K]=3'/></SeekPredicates></IndexScan>");
        var forward = Cardinality().AnalyzeNodeDetailed(op, Ns);
        var reverse = Engine(new CardinalityErrorRule(), new RowEstimateMismatchRule()).AnalyzeNodeDetailed(op, Ns);
        var diagnostic = forward.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Severity.Should().Be(IssueSeverity.Critical);
        diagnostic.Origins.Select(o => o.RuleId).Should().Equal("RULE_004_ESTIMATE_MISMATCH", "RULE_030_CARDINALITY_ERROR");
        diagnostic.Origins.Should().OnlyContain(o => o.RuleVersion == "2.0.0");
        diagnostic.Evidence.Should().Contain(e => e.Name == "ResidualPredicate" && e.Value == "[A]=1 AND [B]=2")
            .And.Contain(e => e.Name == "SeekPredicate" && e.Value == "[K]=3");
        diagnostic.DiagnosticId.Should().Be(reverse.Diagnostics.Single().DiagnosticId);
        forward.Runs.SelectMany(r => r.DiagnosticIds).Distinct().Should().ContainSingle();
        forward.ToLegacyResults().Should().ContainSingle();
    }

    [Theory]
    [InlineData("Index Seek", false)]
    [InlineData("Clustered Index Seek", true)]
    [InlineData("Index Scan", true)]
    [InlineData("Clustered Index Scan", false)]
    [InlineData("Table Scan", true)]
    [InlineData("Columnstore Index Scan", true)]
    public void Residual_LocalPayloadOrDirectPredicateDoesNotRequireSeekScalar(string physical, bool nested)
    {
        string payload = Predicate();
        if (nested) payload = "<IndexScan>" + payload + "</IndexScan>";
        var op = Op(counters: Counter("ActualRows='100' ActualRowsRead='1000'"), payload: payload, physical: physical);
        var report = Residual().AnalyzeNodeDetailed(op, Ns);
        var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.SemanticCode.Should().Be(RowCountRuleEvaluator.ResidualReadCode);
        diagnostic.Origins.Should().HaveCount(2);
        diagnostic.Evidence.Should().Contain(e => e.Name == "RowsReadMinusOutput" && e.Value == "900")
            .And.Contain(e => e.Name == "ReadAmplificationRatio" && e.Value == "10");
        PlanDiagnosticAnalyzer.ExtractResidualPredicate(op, Ns).Should().Be("[T].[V]=(2)");
        new ResidualPredOpRule().Analyze(op, Ns)!.Title.Should().Be("残差谓词读取放大");
    }

    [Theory]
    [InlineData("100", "120", false)]
    [InlineData("100", "200", false)]
    [InlineData("100", "201", true)]
    [InlineData("1000", "1200", false)]
    [InlineData("1000", "1201", true)]
    [InlineData("0", "100", false)]
    [InlineData("0", "101", true)]
    [InlineData("0", "0", false)]
    [InlineData("18446744073709551615", "18446744073709551615", false)]
    public void Residual_ReadThresholdsAndZeroOutputAreExplicit(string output, string read, bool hit)
    {
        var report = Engine(new ResidualPredOpRule()).AnalyzeNodeDetailed(Op(counters:
            Counter($"ActualRows='{output}' ActualRowsRead='{read}'"), payload: Predicate()), Ns);
        report.Runs.Single().Status.Should().Be(hit ? RuleRunStatus.Hit : RuleRunStatus.NoHit);
        if (hit && output == "0")
        {
            var ratio = report.Diagnostics.Single().Evidence.Single(e => e.Name == "ReadAmplificationRatio");
            ratio.Value.Should().BeNull();
            DiagnosticTextFormatter.Format(report).Should().Contain("N/A").And.NotContain("Infinity");
        }
    }

    [Theory]
    [InlineData("", "RULE_MISSING_EVIDENCE")]
    [InlineData("ActualRows='100'", "RULE_MISSING_EVIDENCE")]
    [InlineData("ActualRows='100' ActualRowsRead='NaN'", "RULE_MISSING_EVIDENCE")]
    [InlineData("ActualRows='1000' ActualRowsRead='100'", "RULE_INCONSISTENT_EVIDENCE")]
    [InlineData("ActualRows='100' ActualRowsRead='1000' ActualExecutions='0'", "RULE_INCONSISTENT_EVIDENCE")]
    public void Residual_UnusableRuntimeRetainsPresenceButSkipsMeasuredClaim(string values, string reason)
    {
        var report = Residual().AnalyzeNodeDetailed(Op(counters: values.Length == 0 ? "" : Counter(values), payload: Predicate()), Ns);
        report.Runs.Single(r => r.RuleId == "RULE_034_RESIDUAL_PRED_OP").ReasonCode.Should().Be(reason);
        var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.SemanticCode.Should().Be("RESIDUAL_PREDICATE_PRESENT");
        diagnostic.Evidence.Should().NotContain(e => e.Name == "ReadAmplificationRatio");
    }

    [Fact]
    public void Residual_ValidTotalsCannotHideInconsistentThreadOrIncompleteWorker()
    {
        string first = Counter("Thread='1' ActualRows='100' ActualRowsRead='10'");
        string second = Counter("Thread='2' ActualRows='1' ActualRowsRead='1000'");
        Engine(new ResidualPredOpRule()).AnalyzeNodeDetailed(Op(counters: first + second, payload: Predicate()), Ns)
            .Runs.Single().ReasonCode.Should().Be("RULE_INCONSISTENT_EVIDENCE");
        first = Counter("Thread='1' ActualRows='100'");
        Engine(new ResidualPredOpRule()).AnalyzeNodeDetailed(Op(counters: first + second, payload: Predicate()), Ns)
            .Runs.Single().ReasonCode.Should().Be("RULE_MISSING_EVIDENCE");
    }

    [Theory]
    [InlineData("<IndexScan><SeekPredicates><ScalarOperator ScalarString='[T].[V]=2'/></SeekPredicates></IndexScan>")]
    [InlineData("<IndexScan><RelOp><IndexScan><Predicate><ScalarOperator ScalarString='[child]=1'/></Predicate></IndexScan></RelOp></IndexScan>")]
    [InlineData("<InternalInfo><Predicate><ScalarOperator ScalarString='[internal]=1'/></Predicate></InternalInfo>")]
    [InlineData("<Warnings><Predicate><ScalarOperator ScalarString='[warning]=1'/></Predicate></Warnings>")]
    [InlineData("<Other xmlns='urn:foreign'><Predicate><ScalarOperator ScalarString='[foreign]=1'/></Predicate></Other>")]
    public void Residual_OnlyLocalResidualEvidenceIsApplicable(string payload)
    {
        var op = Op(counters: Counter("ActualRows='100' ActualRowsRead='1000'"), payload: payload);
        Residual().AnalyzeNodeDetailed(op, Ns).Runs.Should().OnlyContain(r => r.Status == RuleRunStatus.NoHit);
        PlanDiagnosticAnalyzer.ExtractResidualPredicate(op, Ns).Should().BeEmpty();
    }

    [Theory]
    [InlineData("<Predicate><ScalarOperator/></Predicate>")]
    [InlineData("<IndexScan><Predicate><ScalarOperator ScalarString='   '/></Predicate></IndexScan>")]
    public void Residual_PredicateWithoutReadableScalarIsMissingEvidence(string payload)
    {
        Residual().AnalyzeNodeDetailed(Op(payload: payload), Ns).Runs.Should()
            .OnlyContain(r => r.Status == RuleRunStatus.Skipped && r.ReasonCode == "RULE_MISSING_EVIDENCE");
    }

    [Theory]
    [InlineData("year([t].[d])=2026", true)]
    [InlineData("CONVERT(int,[t].[s])=1", true)]
    [InlineData("substring([t].[s],1,1)='a'", true)]
    [InlineData("isnull([t].[a],0)=1", true)]
    [InlineData("[t].[YEAR]=2026", false)]
    [InlineData("[t].[s]='YEAR([x])'", false)]
    [InlineData("[t].[d]=CONVERT(date,'20260101')", false)]
    [InlineData("[t].[d]=YEAR('20260101')", false)]
    [InlineData("[t].[YEAR_OTHER]=1", false)]
    [InlineData("YEAR([broken", false)]
    public void Residual_FunctionHintRequiresParsedColumnArgument(string predicate, bool hint)
    {
        var op = Op(payload: Predicate(predicate));
        var report = Engine(new ResidualPredicateRule()).AnalyzeNodeDetailed(op, Ns);
        report.Diagnostics.Single().RuleId.Should().Be(hint ? "RULE_007_NON_SARGABLE" : "RULE_006_RESIDUAL_PREDICATE");
        new ResidualPredicateRule().Analyze(op, Ns)!.RuleId.Should().Be(report.Diagnostics.Single().RuleId);
        report.Diagnostics.Single().Recommendations.Should().Contain(r => r.Contains("INCLUDE") && r.Contains("不保证"));
    }

    [Fact]
    public void Residual_OversizedFunctionHintKeepsObservedPredicateWithoutParsing()
    {
        var op = Op(payload: Predicate("YEAR([x])=1" + new string(' ', 16_384)));
        Engine(new ResidualPredicateRule()).AnalyzeNodeDetailed(op, Ns).Diagnostics.Single().RuleId.Should().Be("RULE_006_RESIDUAL_PREDICATE");
    }

    [Fact]
    public void Residual_UnsupportedPhysicalOperatorIsNotApplicable()
    {
        Residual().AnalyzeNodeDetailed(Op(payload: Predicate(), physical: "Hash Match"), Ns).Runs.Should()
            .OnlyContain(r => r.ReasonCode == "RULE_NOT_APPLICABLE");
    }

    [Fact]
    public void Fixture_MultiStatementNodeIdsAndPrefixedPayloadRemainDistinctAcrossEntriesAndJson()
    {
        var document = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp14_cardinality_residual.sqlplan"));
        var engine = Engine(new RowEstimateMismatchRule(), new CardinalityErrorRule(), new ResidualPredicateRule(), new ResidualPredOpRule());
        var report = engine.AnalyzePlanDetailed(document, Ns);
        report.HasFailures.Should().BeFalse();
        report.Diagnostics.Should().HaveCount(4);
        report.Diagnostics.Should().OnlyContain(d => d.NodeId == "0" && d.Location.Operator!.NodeId == "0");
        report.Runs.Should().OnlyContain(r => r.NodeId == "0");
        report.Diagnostics.Select(d => d.Location.Statement).Distinct().Should().HaveCount(2);
        foreach (var op in document.Descendants(Ns + "RelOp"))
        {
            var nodeReport = engine.AnalyzeNodeDetailed(op, Ns);
            nodeReport.Diagnostics.Select(d => d.DiagnosticId).Should().BeEquivalentTo(report.Diagnostics
                .Where(d => d.Location == nodeReport.Diagnostics[0].Location).Select(d => d.DiagnosticId));
            var residual = nodeReport.Diagnostics.Single(d => d.SemanticCode == RowCountRuleEvaluator.ResidualReadCode);
            residual.Evidence.Where(e => e.Name == "ResidualPredicate").Should().HaveCount(2);
            residual.Evidence.Should().OnlyContain(e => e.Location == residual.Location);
            residual.Origins.Should().HaveCount(2);
        }
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report));
        json.RootElement.GetProperty("Diagnostics").GetArrayLength().Should().Be(4);
        json.RootElement.GetProperty("Diagnostics").EnumerateArray().Should()
            .OnlyContain(e => e.GetProperty("NodeId").GetString() == "0");
        report.ToLegacyResults().Should().HaveCount(4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deduplication_RespectsDisabledRulesAndSeverityOverrides(bool residual)
    {
        string first = residual ? "RULE_006_RESIDUAL_PREDICATE" : "RULE_004_ESTIMATE_MISMATCH";
        string second = residual ? "RULE_034_RESIDUAL_PRED_OP" : "RULE_030_CARDINALITY_ERROR";
        string path = Path.Combine(Path.GetTempPath(), $"imp14-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { Rules = new[]
                { new { RuleId = first, Enabled = false, SeverityOverride = "Info" }, new { RuleId = second, Enabled = true, SeverityOverride = "Info" } } }));
            var engine = new RuleEngine(path);
            engine.RegisterRule(residual ? new ResidualPredicateRule() : new RowEstimateMismatchRule());
            engine.RegisterRule(residual ? new ResidualPredOpRule() : new CardinalityErrorRule());
            var report = engine.AnalyzeNodeDetailed(Op("EstimateRows='1'", Counter("ActualRows='5000' ActualRowsRead='50000' ActualExecutions='1'"), Predicate()), Ns);
            report.Runs.Single(r => r.RuleId == first).ReasonCode.Should().Be("RULE_DISABLED");
            var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
            diagnostic.Severity.Should().Be(IssueSeverity.Info);
            diagnostic.Origins.Should().ContainSingle().Which.RuleId.Should().Be(second);
        }
        finally { File.Delete(path); }
    }
}
