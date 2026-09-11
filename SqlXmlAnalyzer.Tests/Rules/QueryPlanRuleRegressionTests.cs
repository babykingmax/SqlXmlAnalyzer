using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Rules;

public sealed class QueryPlanRuleRegressionTests
{
    private static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;

    [Theory]
    [InlineData("<ColumnsWithNoStatistics><ColumnReference Column='C'/></ColumnsWithNoStatistics>", false)]
    [InlineData("<PlanAffectingConvert ConvertIssue='Cardinality Estimate' Expression='x'/>", false)]
    [InlineData("<MemoryGrantWarning GrantWarningKind='Excessive Grant'/>", false)]
    [InlineData("<NoJoinPredicate/>", false)]
    [InlineData("<SpillToTempDb SpillLevel='1'/>", true)]
    [InlineData("<HashSpillDetails/>", true)]
    [InlineData("<SortSpillDetails/>", true)]
    [InlineData("<ExchangeSpillDetails/>", true)]
    [InlineData("<SpillToTempDb xmlns='urn:foreign'/>", false)]
    public void MemorySpill_OnlyReportsSpillEvidence(string warning, bool hit)
    {
        var document = Plan(Statement("SELECT 1", Query("", $"<RelOp NodeId='7' PhysicalOp='Sort'><Warnings>{warning}</Warnings></RelOp>")));
        var report = Run(document, new MemorySpillRule());
        report.HasFailures.Should().BeFalse();
        report.Diagnostics.Count.Should().Be(hit ? 1 : 0);
    }

    [Theory]
    [InlineData("large")]
    [InlineData("grant")]
    [InlineData("serial")]
    [InlineData("compile")]
    [InlineData("statistics")]
    public void SecondQueryPlan_DiagnosticAndNodeProjectionKeepOwningIdentity(string kind)
    {
        var (rule, attributes, content) = Case(kind);
        var document = Plan(Statement("SELECT 1", Query("", Root())), Statement("SELECT 2", Query(attributes, content + Root())));
        var report = Run(document, rule);
        report.HasFailures.Should().BeFalse();
        report.Runs.Select(run => run.Status).Should().Equal(RuleRunStatus.NoHit, RuleRunStatus.Hit);
        var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Scope.Should().Be(RuleScope.QueryPlan);
        diagnostic.Location.Statement!.StatementOrdinal.Should().Be(2);
        diagnostic.Location.QueryPlan.Should().NotBeNull();
        diagnostic.Location.Operator.Should().BeNull();
        diagnostic.Evidence.Should().OnlyContain(e => e.Location == diagnostic.Location);
        var roots = document.Descendants(Ns + "RelOp").ToList();
        report.ForOperator(roots[0]).Diagnostics.Should().BeEmpty();
        report.ForOperator(roots[1]).Diagnostics.Should().ContainSingle();
        var nodeReport = Engine(rule).AnalyzeNodeDetailed(roots[1], Ns);
        nodeReport.Diagnostics.Single().DiagnosticId.Should().Be(diagnostic.DiagnosticId);
    }

    [Theory]
    [InlineData("large")]
    [InlineData("grant")]
    [InlineData("serial")]
    [InlineData("compile")]
    [InlineData("statistics")]
    public void CursorQueryPlansWithoutOperators_GetSeparateRunsAndEvidence(string kind)
    {
        var (rule, attributes, content) = Case(kind);
        var document = Plan(new XElement(Ns + "StmtCursor", new XAttribute("StatementText", "SELECT 1"),
            new XElement(Ns + "CursorPlan", Query("", ""), Query(attributes, content))));
        var report = Run(document, rule);
        report.HasFailures.Should().BeFalse();
        report.Runs.Select(run => run.Status).Should().Equal(RuleRunStatus.NoHit, RuleRunStatus.Hit);
        report.Diagnostics.Single().Location.QueryPlan!.QueryPlanOrdinal.Should().Be(2);
        document.Descendants(Ns + "RelOp").Should().BeEmpty("synthetic contexts must not alter the input");
    }

    [Fact]
    public void CompileTime_UsesQueryPlanAttributesAndMakesNoCacheMissClaim()
    {
        var document = Plan(Statement("SELECT 1", Query("CompileTime='900' CompileCPU='850'", "<QueryTimeStats CpuTime='2' ElapsedTime='3'/>" + Root())),
            Statement("SELECT 2", Query("", "<QueryTimeStats CompileTime='9000' CompileCPU='8000'/>" + Root())));
        var report = Run(document, new CacheAndRecompileRule());
        report.Runs.Select(run => run.Status).Should().Equal(RuleRunStatus.Hit, RuleRunStatus.NoHit);
        report.Diagnostics.Single().LegacyExplanation.Should().Contain("900").And.Contain("850").And.Contain("不能据此认定");
    }

    [Fact]
    public void SpillInSecondPlan_DoesNotMakeTheFirstPlanSpill()
    {
        var document = Plan(Statement("SELECT 1", Query("", Root())), Statement("SELECT 2", Query("", "<RelOp NodeId='0'><Warnings><SpillToTempDb SpillLevel='1'/></Warnings></RelOp>")));
        Run(document, new LargeMemoryGrantRule()).Runs.Select(run => run.Status).Should().Equal(RuleRunStatus.NoHit, RuleRunStatus.Hit);
    }

    [Fact]
    public void StatisticsParser_ReturnsAllOwnedStatisticsWithoutExtensionOrForeignPlans()
    {
        var document = Plan(Statement("SELECT 1", Query("", Stats("A", 100) + "<RelOp NodeId='0'><InternalInfo><QueryPlan>" + Stats("Fake", 1) + "</QueryPlan></InternalInfo></RelOp>")),
            Statement("SELECT 2", Query("", Stats("B", 1))));
        document.Root!.Add(new XElement("foreign", new XElement(Ns + "QueryPlan", XElement.Parse($"<OptimizerStatsUsage xmlns='{Ns}'><StatisticsInfo Table='Foreign'/></OptimizerStatsUsage>"))));
        StatisticsUsageParser.Parse(document, Ns).Select(stat => stat.Table).Should().Equal("[A]", "[B]");
        var report = Run(document, new StatsUsageRule());
        report.Diagnostics.Single().LegacyExplanation.Should().Contain("UPDATE STATISTICS [Db].[dbo].[B] ([IX_B]) WITH FULLSCAN;").And.NotContain("[[");
    }

    [Fact]
    public void StatisticsWithMissingIdentity_RemainADiagnosticWithoutUnsafeSql()
    {
        var document = Plan(Statement("SELECT 1", Query("", "<OptimizerStatsUsage><StatisticsInfo Table='[T]' Statistics='[IX_T]' SamplingPercent='1'/></OptimizerStatsUsage>" + Root())));
        var report = Run(document, new StatsUsageRule());
        report.HasFailures.Should().BeFalse();
        report.Diagnostics.Single().LegacyExplanation.Should().Contain("未生成 SQL").And.NotContain("UPDATE STATISTICS");
    }

    [Theory]
    [InlineData("[c] LIKE '%abc'", "RULE_013_ANTI_PATTERN")]
    [InlineData("CASE WHEN [c]=1 THEN 0 ELSE 1 END=1", "RULE_013_CASE_IN_PREDICATE")]
    public void AntiPattern_ReportsOnlyTheOperatorOwningThePredicate(string scalar, string id)
    {
        var child = new XElement(Ns + "RelOp", new XAttribute("NodeId", "2"), new XElement(Ns + "IndexScan",
            new XElement(Ns + "Predicate", new XElement(Ns + "ScalarOperator", new XAttribute("ScalarString", scalar)))));
        var query = new XElement(Ns + "QueryPlan", new XElement(Ns + "RelOp", new XAttribute("NodeId", "1"), new XElement(Ns + "Sort", child)));
        var report = Run(Plan(Statement("SELECT 1", query)), new AntiPatternRule());
        report.Diagnostics.Should().ContainSingle().Which.NodeId.Should().Be("2");
        report.Diagnostics.Single().RuleId.Should().Be(id);
    }

    [Theory]
    [InlineData("SELECT (SELECT COUNT(*) FROM dbo.a), (SELECT COUNT(*) FROM dbo.b) FROM dbo.c", true)]
    [InlineData("SELECT (SELECT 1), (SELECT 2)", true)]
    [InlineData("SELECT COALESCE((SELECT MAX(a) FROM dbo.a), 0), (SELECT MAX(b) FROM dbo.b)", true)]
    [InlineData("SELECT '(SELECT x) (SELECT y)' FROM dbo.c", false)]
    [InlineData("SELECT 1 /* (SELECT x) (SELECT y) */ FROM dbo.c", false)]
    [InlineData("SELECT 1 FROM dbo.c WHERE a=(SELECT MAX(a) FROM dbo.a) AND b=(SELECT MAX(b) FROM dbo.b)", false)]
    [InlineData("SELECT (SELECT (SELECT 1) FROM dbo.a) FROM dbo.c", false)]
    [InlineData("SELECT (SELECT 1) UNION ALL SELECT (SELECT 2)", false)]
    public void ScalarSubqueries_AreCountedInIndividualSelectListAsts(string sql, bool hit)
    {
        var report = Run(Plan(Statement(sql, Query("", Root()))), new MultipleScalarSubqueriesRule());
        report.HasFailures.Should().BeFalse();
        report.Runs.Single().Status.Should().Be(hit ? RuleRunStatus.Hit : RuleRunStatus.NoHit);
    }

    [Fact]
    public void InvalidStatementSql_DoesNotPassScalarSubqueryCheck()
    {
        Run(Plan(Statement("SELECT FROM", Query("", Root()))), new MultipleScalarSubqueriesRule())
            .Runs.Single().Status.Should().Be(RuleRunStatus.Skipped);
    }

    [Theory]
    [InlineData("SELECT @p OPTION (OPTIMIZE FOR UNKNOWN)", true)]
    [InlineData("SELECT @p OPTION (OPTIMIZE /* comment */ FOR UNKNOWN)", true)]
    [InlineData("SELECT @p OPTION (OPTIMIZE FOR (@p UNKNOWN))", true)]
    [InlineData("SELECT 'OPTIMIZE FOR UNKNOWN'", false)]
    [InlineData("SELECT @p -- OPTION (OPTIMIZE FOR UNKNOWN)", false)]
    [InlineData("SELECT @p OPTION (OPTIMIZE FOR (@p=1))", false)]
    public void ParameterHint_RequiresAnActualStatementHint(string sql, bool hit)
    {
        var report = Run(Plan(Statement(sql, Query("", "<RelOp NodeId='0'><IndexScan><Predicate><ScalarOperator ScalarString='OPTIMIZE FOR UNKNOWN'/></Predicate></IndexScan></RelOp>"))), new ParameterSniffingRule());
        report.HasFailures.Should().BeFalse();
        report.Runs.Single().Status.Should().Be(hit ? RuleRunStatus.Hit : RuleRunStatus.NoHit);
        if (hit) report.Diagnostics.Single().RuleId.Should().Be("RULE_003_OPTIMIZE_FOR_UNKNOWN");
    }

    [Fact]
    public void ParameterValuesAndStatistics_DoNotLeakBetweenPlans()
    {
        var document = Plan(Statement("SELECT 1", Query("", Stats("A", 100) + Root())), Statement("SELECT @p", Query("",
            "<ParameterList><ColumnReference Column='@p' ParameterCompiledValue='1' ParameterRuntimeValue='2'/></ParameterList>" + Root())));
        var report = Run(document, new ParameterSniffingRule());
        report.Runs.Select(run => run.Status).Should().Equal(RuleRunStatus.NoHit, RuleRunStatus.Hit);
        report.Diagnostics.Single().Location.Statement!.StatementOrdinal.Should().Be(2);
        report.Diagnostics.Single().LegacyExplanation.Should().NotContain("[A]");
    }

    [Fact]
    public void PerParameterUnknown_DoesNotSuppressOtherParameterMismatches()
    {
        var document = Plan(Statement("SELECT @p, @q OPTION (OPTIMIZE FOR (@p UNKNOWN))", Query("",
            "<ParameterList><ColumnReference Column='@p' ParameterCompiledValue='1' ParameterRuntimeValue='2'/>" +
            "<ColumnReference Column='@q' ParameterCompiledValue='10' ParameterRuntimeValue='999'/></ParameterList>" + Root())));
        var diagnostic = Run(document, new ParameterSniffingRule()).Diagnostics.Single();
        diagnostic.RuleId.Should().Be("RULE_003_PARAM_SNIFFING");
        diagnostic.LegacyExplanation.Should().Contain("@q (编译值: 10, 运行值: 999)").And.NotContain("@p (编译值:");
    }

    private static (IPlanAnalyzerRule Rule, string Attributes, string Content) Case(string kind) => kind switch
    {
        "large" => (new LargeMemoryGrantRule(), "", "<MemoryGrantInfo GrantedMemory='102400' MaxUsedMemory='100'/>"),
        "grant" => (new MemoryGrantDocRule(), "", "<MemoryGrantInfo GrantedMemory='102400' MaxUsedMemory='100'/>"),
        "serial" => (new SerialPlanReasonRule(), "NonParallelPlanReason='MaxDOPSetToOne'", ""),
        "compile" => (new CacheAndRecompileRule(), "CompileTime='1000' CompileCPU='900'", ""),
        "statistics" => (new StatsUsageRule(), "", Stats("B", 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static string Root() => "<RelOp NodeId='0' PhysicalOp='Constant Scan' EstimateRows='1'/>";
    private static string Stats(string table, int sampling) => $"<OptimizerStatsUsage><StatisticsInfo Database='[Db]' Schema='[dbo]' Table='[{table}]' Statistics='[IX_{table}]' SamplingPercent='{sampling}'/></OptimizerStatsUsage>";
    private static XElement Query(string attributes, string content) => XElement.Parse($"<QueryPlan xmlns='{Ns}' {attributes}>{content}</QueryPlan>");
    private static XElement Statement(string sql, XElement query) => new(Ns + "StmtSimple", new XAttribute("StatementText", sql), query);
    private static XDocument Plan(params XElement[] statements) => new(new XElement(Ns + "ShowPlanXML", new XElement(Ns + "BatchSequence", new XElement(Ns + "Batch", new XElement(Ns + "Statements", statements)))));
    private static RuleEngine Engine(IPlanAnalyzerRule rule) { var engine = new RuleEngine(configuration: RuleConfigurationDocument.Defaults); engine.RegisterRule(rule); return engine; }
    private static PlanDiagnosticReport Run(XDocument document, IPlanAnalyzerRule rule) => Engine(rule).AnalyzePlanDetailed(document, Ns);
}
