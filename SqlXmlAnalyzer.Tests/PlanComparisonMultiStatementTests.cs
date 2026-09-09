using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Comparison;
using System.IO;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanComparisonMultiStatementTests
{
    internal static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    [Fact]
    public void AllStatements_CanBeComparedWithoutSelectingTheFirstQueryPlan()
    {
        var a = Snapshot(Statement("SELECT 1", 1) + Statement("SELECT 2", 2));
        var b = Snapshot(Statement("SELECT 1", 1) + Statement("SELECT 2", 999));
        Action compare = () => new PlanComparisonController().BuildComparison(a, b, Ns);
        compare.Should().NotThrow();
        var result = new PlanComparisonController().BuildComparison(a, b, Ns);
        result.Statements.Should().HaveCount(2);
        result.Statements[1].RootsA.Single().Cost.Should().Be(2);
        var changed = result.Statements[1].RootsB.Single();
        changed.Cost.Should().Be(999);
        changed.CostDelta.Should().Be(997);
        changed.CostPercentDelta.Should().Be(49850);
        changed.OtherIdentity.Should().Be(result.Statements[1].RootsA.Single().Identity);
        result.PlanA.Should().BeNull("旧单根兼容字段不能悄悄隐藏后续语句");
    }

    [Fact]
    public void Statements_ReorderingInsertionAndDeletionKeepUniqueCorrespondence()
    {
        var a = Snapshot(Statement("SELECT 1", 1) + Statement("SELECT 2", 2) + Statement("SELECT 3", 3));
        var b = Snapshot(Statement("SELECT 2", 999) + Statement("SELECT 4", 4) + Statement("SELECT 1", 1));
        var result = Compare(a, b);
        result.Statements.Should().HaveCount(4);
        result.Statements[0].B!.Statement.Text.Should().Be("SELECT 1");
        result.Statements[1].B!.Statement.Key.StatementOrdinal.Should().Be(1);
        result.Statements[1].RootsB.Single().CostDelta.Should().Be(997);
        result.Statements.Count(s => s.A == null).Should().Be(1);
        result.Statements.Count(s => s.B == null).Should().Be(1);
    }

    [Fact]
    public void DuplicateSql_RemainsUnmatchedInsteadOfGreedyPairingByPosition()
    {
        var result = Compare(Snapshot(Statement("SELECT 1", 1) + Statement("SELECT 1", 2)),
            Snapshot(Statement("SELECT 1", 8) + Statement("SELECT 1", 9)));
        result.Statements.Should().HaveCount(4);
        result.Statements.Should().OnlyContain(s => s.Confidence == ComparisonConfidence.Unmatched);
        result.Statements.SelectMany(s => s.RootsB).Should().OnlyContain(n => n.OtherIdentity == null && n.CostDelta == null);
    }

    [Fact]
    public void QueryHashWithDifferentLiterals_IsOnlyACandidate()
    {
        var a = Snapshot(Statement("SELECT 1", 1)); var b = Snapshot(Statement("SELECT 2", 2));
        foreach (var snapshot in new[] { a, b }) snapshot.Document.Descendants(Ns + "StmtSimple").Single().SetAttributeValue("QueryHash", "0x1234567890ABCDEF");
        var pair = Compare(a, b).Statements.Single();
        pair.Confidence.Should().Be(ComparisonConfidence.Candidate);
        pair.Conditions.EstimatesComparable.Should().BeFalse();
        pair.RootsB.Single().CostDelta.Should().BeNull();
    }

    [Fact]
    public void SqlWhitespaceAndCommentsDoNotBreakMatching_ButLiteralContentsDo()
    {
        Compare(Snapshot(Statement("SELECT 1", 1)), Snapshot(Statement("SELECT /* note */  1", 2)))
            .Statements.Single().Confidence.Should().Be(ComparisonConfidence.High);
        Compare(Snapshot(Statement("SELECT 'A'", 1)), Snapshot(Statement("SELECT 'a'", 2)))
            .Statements.Should().HaveCount(2);
    }

    [Fact]
    public void SameSqlWithDifferentDatabases_DoesNotProduceComparableMetrics()
    {
        var a = Snapshot(Statement("SELECT * FROM dbo.T", 1, "T")); var b = Snapshot(Statement("SELECT * FROM dbo.T", 2, "T"));
        b.Document.Descendants(Ns + "Object").Single().SetAttributeValue("Database", "[other]");
        var pair = Compare(a, b).Statements.Single();
        pair.Confidence.Should().Be(ComparisonConfidence.Candidate);
        pair.Conditions.EstimatesComparable.Should().BeFalse();
    }

    [Theory]
    [InlineData("Build", "17.0.1.1")]
    [InlineData("Build", null)]
    [InlineData("CardinalityEstimationModelVersion", "150")]
    [InlineData("CardinalityEstimationModelVersion", null)]
    [InlineData("ANSI_NULLS", "false")]
    [InlineData("ANSI_NULLS", null)]
    [InlineData("ANSI_NULLS", "garbage")]
    [InlineData("DatabaseCompatibilityLevel", "160")]
    public void CapturedContext_MissingOrDifferentValuesSuppressDeltas(string name, string? value)
    {
        var a = RuntimeSnapshot(100); var b = RuntimeSnapshot(10);
        var element = name == "Build" ? b.Document.Root! : name == "ANSI_NULLS"
            ? b.Document.Descendants(Ns + "StatementSetOptions").Single() : b.Document.Descendants(Ns + "StmtSimple").Single();
        element.SetAttributeValue(name, value);
        var pair = Compare(a, b).Statements.Single();
        pair.RootsB.Single().CostDelta.Should().BeNull();
        pair.RootsB.Single().RuntimeDeltas.Should().OnlyContain(d => d.Delta == null);
        pair.Conditions.Reasons.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("DegreeOfParallelism", "2")]
    [InlineData("DegreeOfParallelism", null)]
    [InlineData("ActualExecutions", "2")]
    [InlineData("ActualExecutions", null)]
    [InlineData("ActualExecutionMode", "Batch")]
    [InlineData("ActualExecutionMode", null)]
    [InlineData("ActualRows", null)]
    [InlineData("Parallel", "true")]
    public void RuntimeExecutionBasis_MustMatchIndependentlyOfEstimatedCosts(string name, string? value)
    {
        var a = RuntimeSnapshot(100); var b = RuntimeSnapshot(10);
        var element = b.Document.Descendants(Ns + (name == "DegreeOfParallelism" ? "QueryPlan" : name == "Parallel" ? "RelOp" : "RunTimeCountersPerThread")).Single();
        element.SetAttributeValue(name, value);
        var pair = Compare(a, b).Statements.Single();
        pair.RootsB.Single().CostDelta.Should().Be(0);
        pair.RootsB.Single().RuntimeDeltas.Should().OnlyContain(d => d.Delta == null);
    }

    [Fact]
    public void ActualVersusEstimated_KeepsObservedValuesWithoutInventingRuntimeDelta()
    {
        var a = Snapshot(Statement("SELECT 1", 1)); var b = RuntimeSnapshot(10);
        var pair = Compare(a, b).Statements.Single();
        pair.Conditions.EstimatesComparable.Should().BeTrue();
        pair.Conditions.RuntimeComparable.Should().BeFalse();
        pair.RootsB.Single().RuntimeDeltas.Should().Contain(d => d.Label == "Elapsed" && d.Value == 10 && d.Delta == null);
    }

    [Theory]
    [InlineData("ParameterCompiledValue", "(2)", false)]
    [InlineData("ParameterRuntimeValue", "(2)", true)]
    [InlineData("ParameterRuntimeValue", null, true)]
    [InlineData("ParameterDataType", "bigint", false)]
    public void Parameters_ArePreservedAndChecked(string attribute, string? value, bool estimates)
    {
        var a = RuntimeSnapshot(100); var b = RuntimeSnapshot(10);
        foreach (var snapshot in new[] { a, b }) snapshot.Document.Descendants(Ns + "ParameterList").Single()
            .Add(new XElement(Ns + "ColumnReference", new XAttribute("Column", "@p"), new XAttribute("ParameterDataType", "int"),
                new XAttribute("ParameterCompiledValue", "(1)"), new XAttribute("ParameterRuntimeValue", "(1)")));
        b.Document.Descendants(Ns + "ColumnReference").Single().SetAttributeValue(attribute, value);
        var pair = Compare(a, b).Statements.Single();
        pair.QueryA!.Capture.Parameters.Single().CompiledValue.Should().Be("(1)");
        pair.Conditions.EstimatesComparable.Should().Be(estimates);
        pair.Conditions.RuntimeComparable.Should().BeFalse();
    }

    [Fact]
    public void MissingParameterListIsUnknown_NotEquivalentToAnExplicitEmptyList()
    {
        var a = RuntimeSnapshot(100); var b = RuntimeSnapshot(10);
        b.Document.Descendants(Ns + "ParameterList").Remove();
        Compare(a, b).Statements.Single().Conditions.EstimatesComparable.Should().BeFalse();
    }

    [Fact]
    public void CompleteRuntimeMetrics_HaveUnitsSourcesAndDefinedBMinusAChange()
    {
        var pair = Compare(RuntimeSnapshot(100), RuntimeSnapshot(10)).Statements.Single();
        var elapsed = pair.RootsB.Single().RuntimeDeltas.Single(d => d.Label == "Elapsed");
        elapsed.Delta.Should().Be(-90); elapsed.PercentDelta.Should().Be(-90);
        elapsed.OtherValue.Should().Be(100); elapsed.Source.Should().Contain("ms").And.Contain("MaxThreads");
    }

    [Fact]
    public void ZeroBaseline_PreservesAbsoluteDeltaButHasNoPercentage()
    {
        var a = Snapshot(Statement("SELECT 1", 0, runtime: "ActualElapsedms='0'"));
        var b = Snapshot(Statement("SELECT 1", 10, runtime: "ActualElapsedms='10'"));
        var result = Compare(a, b);
        var node = result.Statements.Single().RootsB.Single();
        node.CostDelta.Should().Be(10); node.CostPercentDelta.Should().BeNull();
        node.RuntimeDeltas.Single(d => d.Label == "Elapsed").PercentDelta.Should().BeNull();
        new PlanComparisonTreeService().BuildTree(result).PlanB!.CostText.Should().Contain("差值 +10").And.Contain("百分比 N/A");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    public void MissingOrInvalidCosts_AreNeverTurnedIntoZero(string? cost)
    {
        var a = RuntimeSnapshot(100); var b = RuntimeSnapshot(10);
        b.Document.Descendants(Ns + "RelOp").Single().SetAttributeValue("EstimatedTotalSubtreeCost", cost);
        var result = Compare(a, b);
        result.Statements.Single().RootsB.Single().Cost.Should().BeNull();
        result.Statements.Single().RootsB.Single().CostDelta.Should().BeNull();
        new PlanComparisonTreeService().BuildTree(result).PlanB!.CostText.Should().Contain("成本: N/A");
    }

    [Fact]
    public void ReorderedChildrenMatchByObjects_NotNodeIdsOrPosition()
    {
        var a = JoinSnapshot(false); var b = JoinSnapshot(true);
        var node = Compare(a, b).Statements.Single().RootsB.Single();
        node.Children[0].Source.Descendants(Ns + "Object").Single().Attribute("Table")!.Value.Should().Be("[U]");
        node.Children[0].OtherIdentity!.OperatorOrdinal.Should().Be(3);
        node.Children[1].OtherIdentity!.OperatorOrdinal.Should().Be(2);
    }

    [Fact]
    public void DuplicateOperatorsWithinAQuery_AreVisibleAsUnmatched()
    {
        var a = JoinSnapshot(false); var b = JoinSnapshot(false);
        foreach (var snapshot in new[] { a, b }) snapshot.Document.Descendants(Ns + "Object").Last().SetAttributeValue("Table", "[T]");
        var node = Compare(a, b).Statements.Single().RootsB.Single();
        node.Children.Should().OnlyContain(c => c.OtherIdentity == null && c.CostDelta == null);
    }

    [Fact]
    public void MultipleQueryPlansAndStatementsWithoutPlans_RemainVisible()
    {
        var a = Snapshot(Statement("SELECT 1", 1) + "<StmtSimple StatementText='PRINT 1'/>");
        a.Document.Descendants(Ns + "StmtSimple").First().Add(new XElement(a.Document.Descendants(Ns + "QueryPlan").Single()));
        var b = new PlanSnapshot { Document = new XDocument(a.Document) };
        var result = Compare(a, b);
        result.Statements.Should().HaveCount(5, "两个重复 QueryPlan 各自未匹配，加一个无计划语句");
        result.Statements.Last().Detail.Should().Contain("没有可比较");
    }

    [Fact]
    public void ManualSelectionMatchesAnyChosenStatement_AndStillChecksContext()
    {
        var a = Snapshot(Statement("SELECT 1", 1) + Statement("SELECT 2", 2));
        var b = Snapshot(Statement("SELECT 3", 3) + Statement("SELECT 4", 4));
        a.SelectedQueryPlan = a.IdentityModel!.QueryPlans[1].Key; b.SelectedQueryPlan = b.IdentityModel!.QueryPlans[0].Key;
        b.Document.Root!.SetAttributeValue("Build", "17.0.1.1");
        b.SelectedQueryPlan = b.IdentityModel!.QueryPlans[0].Key;
        var pair = Compare(a, b).Statements.Single();
        pair.Confidence.Should().Be(ComparisonConfidence.Manual);
        pair.A!.Statement.Text.Should().Be("SELECT 2"); pair.B!.Statement.Text.Should().Be("SELECT 3");
        pair.Conditions.EstimatesComparable.Should().BeFalse();
    }

    [Fact]
    public void InternalInfoCannotIntroduceFakeStatements()
    {
        var a = RuntimeSnapshot(10);
        a.Document.Descendants(Ns + "StmtSimple").Single().Add(SafeXmlHelper.ParseSafe($"<InternalInfo xmlns='{Ns}'><Statements>{Statement("SELECT 999", 999)}</Statements></InternalInfo>").Root!);
        Compare(a, RuntimeSnapshot(20)).Statements.Should().ContainSingle();
    }

    [Fact]
    public void ResultCaptureIsImmutableAfterSourceChanges_AndOldSelectionsAreRejected()
    {
        var a = RuntimeSnapshot(10); var b = RuntimeSnapshot(20);
        var result = Compare(a, b); a.SelectedQueryPlan = a.IdentityModel!.QueryPlans.Single().Key;
        a.Document.Root!.SetAttributeValue("Build", "17.0.1.1");
        result.Statements.Single().QueryA!.Capture.EngineBuild.Should().Be("16.0.1000.6");
        Action stale = () => Compare(a, b);
        stale.Should().Throw<InvalidDataException>().WithMessage("PLAN_SELECTION_NOT_FOUND*");
    }

    [Fact]
    public void EmbeddedMultiStatementFixture_ExposesSecondChangeAndUncertainCandidate()
    {
        var a = new PlanSnapshot { Document = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp17_comparison_a.sqlplan")) };
        var b = new PlanSnapshot { Document = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp17_comparison_b.sqlplan")) };
        var result = Compare(a, b);
        result.Statements[1].RootsB.Single().CostDelta.Should().Be(997);
        result.Statements[1].RootsB.Single().RuntimeDeltas.Single(d => d.Label == "Elapsed").Delta.Should().Be(-25);
        result.Statements[2].Confidence.Should().Be(ComparisonConfidence.Candidate);
        result.Statements[2].RootsB.Single().CostDelta.Should().BeNull();
        result.Statements[0].QueryA!.Capture.SourceHash.Should().HaveLength(64);
        result.Statements[0].QueryA!.Capture.SourceHashKind.Should().Be("XML 表示");
    }

    [Fact]
    public void NativeSqlServerCapture_ReorderedStatementsKeepCapturedIdentityPairs()
    {
        var a = new PlanSnapshot { Document = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp16_review_predicates.sqlplan")) };
        var b = new PlanSnapshot { Document = new XDocument(a.Document) };
        var statements = b.Document.Descendants(Ns + "Statements").First();
        statements.ReplaceNodes(statements.Elements().Reverse().ToArray());
        var result = Compare(a, b);
        var expected = a.IdentityModel!.Statements.Count;
        result.Statements.Should().HaveCount(expected);
        result.Statements.Should().OnlyContain(s => s.A != null && s.B != null && s.A.Statement.Text == s.B.Statement.Text);
        result.Statements.First().B!.Statement.Key.StatementOrdinal.Should().Be(expected);
        result.Statements.Where(s => s.QueryB != null).Should().OnlyContain(s => !s.Conditions.RuntimeComparable);
    }

    [Fact]
    public void SessionPreservesOriginalHashContextAndManualSelection_AndCountsAllRootCosts()
    {
        var service = new TuningSessionService();
        var snapshot = service.CaptureSnapshot(Snapshot(Statement("SELECT 1", 1) + Statement("SELECT 2", 999)).Document, "imp17.sqlplan", 1);
        snapshot.TotalCost.Should().Be(1000);
        snapshot.SelectedQueryPlan = snapshot.IdentityModel!.QueryPlans[1].Key;
        string path = Path.Combine(Path.GetTempPath(), "imp17-" + Guid.NewGuid().ToString("N") + ".pesession");
        try
        {
            service.Save(path, [snapshot], snapshot, snapshot);
            var loaded = service.Load(path).PlanA!;
            loaded.TotalCost.Should().Be(1000); loaded.OriginalSourceHash.Should().Be(snapshot.OriginalSourceHash);
            loaded.SelectedQueryPlan!.Statement.StatementOrdinal.Should().Be(2);
            Compare(loaded, loaded).Statements.Single().QueryA!.Capture.CardinalityEstimator.Should().Be("160");
        }
        finally { File.Delete(path); }
    }

    internal static PlanComparisonResult Compare(PlanSnapshot a, PlanSnapshot b) => new PlanComparisonController().BuildComparison(a, b, Ns);
    internal static PlanSnapshot RuntimeSnapshot(int elapsed) => Snapshot(Statement("SELECT 1", 1, runtime: $"ActualElapsedms='{elapsed}' ActualLogicalReads='20' ActualRowsRead='2'"));
    private static PlanSnapshot JoinSnapshot(bool reverse)
    {
        var snapshot = Snapshot(Statement("SELECT * FROM dbo.T JOIN dbo.U ON T.K=U.K", 10));
        var root = snapshot.Document.Descendants(Ns + "RelOp").Single();
        root.SetAttributeValue("PhysicalOp", "Hash Match"); root.SetAttributeValue("LogicalOp", "Inner Join");
        root.Element(Ns + "ConstantScan")!.Remove();
        var first = Snapshot(Statement("SELECT 1", 1, "T")).Document.Descendants(Ns + "RelOp").Single();
        var second = Snapshot(Statement("SELECT 1", 2, "U")).Document.Descendants(Ns + "RelOp").Single();
        root.Add(new XElement(Ns + "Hash", reverse ? new[] { new XElement(second), new XElement(first) } : new[] { new XElement(first), new XElement(second) }));
        return snapshot;
    }

    internal static PlanSnapshot Snapshot(string statements) => new()
    {
        Document = SafeXmlHelper.ParseSafe($"<ShowPlanXML xmlns='{Ns}' Version='1.6' Build='16.0.1000.6'><BatchSequence><Batch><Statements>{statements}</Statements></Batch></BatchSequence></ShowPlanXML>")
    };

    internal static string Statement(string sql, double cost, string? table = null, string? runtime = null)
    {
        // Object-free SELECT fixtures are Constant Scans, not damaged base-table accesses.
        string operation = table == null ? "Constant Scan" : "Table Scan";
        string payload = table == null ? "<ConstantScan/>" : $"<TableScan><Object Database='[db]' Schema='[dbo]' Table='[{table}]'/></TableScan>";
        return $"<StmtSimple StatementText='{System.Security.SecurityElement.Escape(sql)}' CardinalityEstimationModelVersion='160'><StatementSetOptions ANSI_NULLS='true' ANSI_PADDING='true' ANSI_WARNINGS='true' ARITHABORT='true' CONCAT_NULL_YIELDS_NULL='true' NUMERIC_ROUNDABORT='false' QUOTED_IDENTIFIER='true'/><QueryPlan DegreeOfParallelism='1'><ParameterList/><RelOp NodeId='0' PhysicalOp='{operation}' LogicalOp='{operation}' Parallel='false' EstimatedTotalSubtreeCost='{cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}'><OutputList/>{(runtime == null ? "" : $"<RunTimeInformation><RunTimeCountersPerThread Thread='0' ActualRows='1' ActualExecutions='1' ActualExecutionMode='Row' {runtime}/></RunTimeInformation>")}{payload}</RelOp></QueryPlan></StmtSimple>";
    }
}
