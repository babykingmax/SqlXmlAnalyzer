using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Comparison;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanComparisonEvidenceHardeningTests
{
    private static readonly XNamespace Ns = PlanComparisonMultiStatementTests.Ns;

    [Theory]
    [InlineData("column")]
    [InlineData("constant")]
    [InlineData("role")]
    public void DifferentBuildResidual_DoesNotProduceOperatorDeltas(string change)
    {
        var a = Hash(100); var b = Hash(10);
        var residual = b.Document.Descendants(Ns + "BuildResidual").Single();
        if (change == "column") residual.Descendants(Ns + "ColumnReference").Single().SetAttributeValue("Column", "J");
        else if (change == "constant") residual.Descendants(Ns + "Const").Single().SetAttributeValue("ConstValue", "(2)");
        else residual.Name = Ns + "ProbeResidual";
        AssertNoDeltas(Compare(a, b).RootsB.Single());
    }

    [Fact]
    public void CompleteBuildResidual_WithoutRenderedScalarString_RemainsComparable()
    {
        var a = Hash(100); var b = Hash(10);
        b.Document.Descendants(Ns + "ScalarOperator").Attributes("ScalarString").Remove();
        AssertComparable(Compare(a, b).RootsB.Single());
    }

    [Theory]
    [InlineData("IS NULL")]
    [InlineData("IS NOT NULL")]
    public void UnaryNullBuildResidual_RemainsComparable(string operation)
    {
        var a = Hash(100); var b = Hash(10);
        foreach (var snapshot in new[] { a, b })
        {
            var comparison = snapshot.Document.Descendants(Ns + "Compare").Single();
            comparison.SetAttributeValue("CompareOp", operation);
            comparison.Elements().Last().Remove();
        }
        AssertComparable(Compare(a, b).RootsB.Single());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing-constant")]
    [InlineData("empty-column")]
    [InlineData("unknown-expression")]
    [InlineData("foreign-expression")]
    [InlineData("foreign-residual")]
    [InlineData("missing-operand")]
    [InlineData("missing-identifier")]
    [InlineData("missing-comparison")]
    [InlineData("unknown-comparison")]
    public void IncompleteBuildResidual_IsNotHighEvenWhenBothSidesHaveTheSameDamage(string damage)
    {
        var a = Hash(100); var b = Hash(10);
        foreach (var snapshot in new[] { a, b })
        {
            var residual = snapshot.Document.Descendants(Ns + "BuildResidual").Single();
            switch (damage)
            {
                case "empty": residual.RemoveNodes(); break;
                case "missing-constant": residual.Descendants(Ns + "Const").Single().RemoveAttributes(); break;
                case "empty-column": residual.Descendants(Ns + "ColumnReference").Single().SetAttributeValue("Column", ""); break;
                case "unknown-expression": residual.Descendants(Ns + "Compare").Single().Name = Ns + "UnknownExpression"; break;
                case "foreign-residual": residual.Name = XName.Get("BuildResidual", "urn:extension"); break;
                case "missing-operand": residual.Descendants(Ns + "Compare").Single().Elements().First().Remove(); break;
                case "missing-identifier": residual.Descendants(Ns + "Identifier").Single().RemoveNodes(); break;
                case "missing-comparison": residual.Descendants(Ns + "Compare").Single().Attribute("CompareOp")!.Remove(); break;
                case "unknown-comparison": residual.Descendants(Ns + "Compare").Single().SetAttributeValue("CompareOp", "UNKNOWN"); break;
                default: residual.Descendants(Ns + "Compare").Single().Name = XName.Get("Compare", "urn:extension"); break;
            }
        }
        var node = Compare(a, b).RootsB.Single();
        node.Confidence.Should().Be(ComparisonConfidence.Candidate);
        AssertNoDeltas(node);
        node.Children.Should().OnlyContain(c => c.Confidence == ComparisonConfidence.High);
    }

    [Theory]
    [InlineData("column")]
    [InlineData("direction")]
    [InlineData("distinct")]
    [InlineData("order")]
    public void DifferentSortSemantics_DoNotProduceOperatorDeltas(string change)
    {
        var a = Sort(100); var b = Sort(10);
        var sort = b.Document.Descendants(Ns + "Sort").Single();
        var order = sort.Element(Ns + "OrderBy")!;
        switch (change)
        {
            case "column": order.Descendants(Ns + "ColumnReference").First().SetAttributeValue("Column", "X"); break;
            case "direction": order.Elements().First().SetAttributeValue("Ascending", "false"); break;
            case "distinct": sort.SetAttributeValue("Distinct", "true"); break;
            default: order.ReplaceNodes(order.Elements().Reverse().ToArray()); break;
        }
        var node = Compare(a, b).RootsB.Single();
        AssertNoDeltas(node);
        node.Children.Single().Confidence.Should().Be(ComparisonConfidence.High);
    }

    [Fact]
    public void SortBooleanLexicalFormsAndAttributeOrder_DoNotChangeIdentity()
    {
        var a = Sort(100); var b = Sort(10);
        var sort = b.Document.Descendants(Ns + "Sort").Single();
        sort.SetAttributeValue("Distinct", "0");
        foreach (var entry in sort.Descendants(Ns + "OrderByColumn")) entry.SetAttributeValue("Ascending", "1");
        foreach (var column in sort.Descendants(Ns + "ColumnReference"))
        {
            var attributes = column.Attributes().Reverse().Select(v => new XAttribute(v)).ToArray();
            column.RemoveAttributes(); column.Add(attributes);
        }
        AssertComparable(Compare(a, b).RootsB.Single());
    }

    [Theory]
    [InlineData("sort")]
    [InlineData("order-by")]
    [InlineData("empty-order")]
    [InlineData("duplicate-order")]
    [InlineData("direction")]
    [InlineData("invalid-direction")]
    [InlineData("distinct")]
    [InlineData("invalid-distinct")]
    [InlineData("column")]
    [InlineData("foreign-column")]
    [InlineData("unknown-expression")]
    public void IncompleteSort_IsOnlyACandidateAndCannotBeRepairedByChildEvidence(string damage)
    {
        var a = Sort(100); var b = Sort(10);
        foreach (var snapshot in new[] { a, b })
        {
            var sort = snapshot.Document.Descendants(Ns + "Sort").Single();
            var order = sort.Element(Ns + "OrderBy")!;
            switch (damage)
            {
                case "sort": sort.Name = Ns + "UnknownSort"; break;
                case "order-by": order.Remove(); break;
                case "empty-order": order.RemoveNodes(); break;
                case "duplicate-order": sort.Add(new XElement(order)); break;
                case "direction": order.Elements().First().Attribute("Ascending")!.Remove(); break;
                case "invalid-direction": order.Elements().First().SetAttributeValue("Ascending", "ascending"); break;
                case "distinct": sort.Attribute("Distinct")!.Remove(); break;
                case "invalid-distinct": sort.SetAttributeValue("Distinct", "yes"); break;
                case "column": order.Descendants(Ns + "ColumnReference").First().RemoveAttributes(); break;
                case "foreign-column": order.Descendants(Ns + "ColumnReference").First().Name = XName.Get("ColumnReference", "urn:extension"); break;
                default: order.Descendants(Ns + "ColumnReference").First().Add(new XElement(Ns + "UnknownExpression")); break;
            }
        }
        var node = Compare(a, b).RootsB.Single();
        node.Confidence.Should().Be(ComparisonConfidence.Candidate);
        AssertNoDeltas(node);
    }

    [Theory]
    [InlineData("rows", "20")]
    [InlineData("ties", "true")]
    [InlineData("rows", null)]
    [InlineData("rows", "invalid")]
    [InlineData("rows", "-1")]
    [InlineData("ties", "invalid")]
    public void TopSortRowsAndTies_AreComparedAndValidated(string field, string? value)
    {
        var a = Sort(100, top: true); var b = Sort(10, top: true);
        b.Document.Descendants(Ns + "TopSort").Single().SetAttributeValue(field == "rows" ? "Rows" : "WithTies", value);
        AssertNoDeltas(Compare(a, b).RootsB.Single());
    }

    [Fact]
    public void CompleteTopSort_RemainsComparable() => AssertComparable(Compare(Sort(100, true), Sort(10, true)).RootsB.Single());

    [Fact]
    public void IncompleteChildSort_PreventsParentHighConfidence()
    {
        var a = Sort(100); var b = Sort(10);
        foreach (var snapshot in new[] { a, b })
        {
            var sort = snapshot.Document.Descendants(Ns + "Sort").Single();
            sort.Attribute("Distinct")!.Remove();
            var root = snapshot.Document.Descendants(Ns + "RelOp").First();
            var parent = new XElement(Ns + "RelOp", new XAttribute("PhysicalOp", "Compute Scalar"),
                new XAttribute("LogicalOp", "Compute Scalar"), new XAttribute("EstimatedTotalSubtreeCost", "2"),
                new XElement(Ns + "ComputeScalar", new XElement(root)));
            root.ReplaceWith(parent);
        }
        var node = Compare(a, b).RootsB.Single();
        node.Confidence.Should().Be(ComparisonConfidence.Candidate);
        AssertNoDeltas(node);
    }

    [Theory]
    [InlineData("Database", null)]
    [InlineData("Schema", null)]
    [InlineData("Table", null)]
    [InlineData("Database", "")]
    [InlineData("Schema", "[]")]
    [InlineData("Table", " ")]
    [InlineData("Object", null)]
    public void MissingObjectIdentity_BlocksAutomaticAndManualDeltas(string field, string? value)
    {
        var a = Scan(100); var b = Scan(10);
        foreach (var snapshot in new[] { a, b })
        {
            var obj = snapshot.Document.Descendants(Ns + "Object").Single();
            if (field == "Object") obj.Remove(); else obj.SetAttributeValue(field, value);
        }
        var automatic = Compare(a, b);
        automatic.Confidence.Should().Be(ComparisonConfidence.Candidate);
        AssertUnknownObject(automatic);
        a.SelectedQueryPlan = a.IdentityModel!.QueryPlans.Single().Key;
        b.SelectedQueryPlan = b.IdentityModel!.QueryPlans.Single().Key;
        var manual = Compare(a, b);
        manual.Confidence.Should().Be(ComparisonConfidence.Manual);
        AssertUnknownObject(manual);
    }

    [Fact]
    public void CompleteLocalObjects_WithoutServer_RemainComparable()
    {
        var pair = Compare(Scan(100), Scan(10));
        pair.Conditions.RuntimeComparable.Should().BeTrue();
        AssertComparable(pair.RootsB.Single());
    }

    [Theory]
    [InlineData("[other]")]
    [InlineData(null)]
    public void CapturedServerDifference_IsNotIgnored(string? server)
    {
        var a = Scan(100); var b = Scan(10);
        a.Document.Descendants(Ns + "Object").Single().SetAttributeValue("Server", "[server]");
        b.Document.Descendants(Ns + "Object").Single().SetAttributeValue("Server", server);
        var pair = Compare(a, b);
        pair.Conditions.EstimatesComparable.Should().BeFalse();
        AssertNoDeltas(pair.RootsB.Single());
    }

    [Fact]
    public void ConstantScans_DoNotRequireAnObject()
    {
        var pair = Compare(Constant(100), Constant(10));
        pair.Conditions.RuntimeComparable.Should().BeTrue();
        AssertComparable(pair.RootsB.Single());
    }

    [Fact]
    public void UnknownObjectUnderSort_AlsoSuppressesAncestorDeltas()
    {
        var a = Sort(100); var b = Sort(10);
        foreach (var snapshot in new[] { a, b }) snapshot.Document.Descendants(Ns + "Object").Single().Attribute("Database")!.Remove();
        var pair = Compare(a, b);
        AssertUnknownObject(pair);
        AssertNoDeltas(pair.RootsB.Single().Children.Single());
    }

    [Fact]
    public void IncompleteSortAmongMultipleQueryPlans_RemainsCandidate()
    {
        var a = Sort(100); var b = Sort(10);
        foreach (var snapshot in new[] { a, b })
        {
            snapshot.Document.Descendants(Ns + "Sort").Single().Attribute("Distinct")!.Remove();
            snapshot.Document.Descendants(Ns + "StmtSimple").Single().Add(
                new XElement(Scan(30, "U").Document.Descendants(Ns + "QueryPlan").Single()));
        }
        var pairs = PlanComparisonMultiStatementTests.Compare(a, b).Statements;
        pairs.Should().HaveCount(2);
        pairs[0].Confidence.Should().Be(ComparisonConfidence.Candidate);
        AssertNoDeltas(pairs[0].RootsB.Single());
        pairs[1].Confidence.Should().Be(ComparisonConfidence.High);
        pairs[1].RootsB.Single().CostDelta.Should().Be(0);
    }

    [Fact]
    public void SortNormalization_DoesNotMutateEitherSourceOrItsIdentityModel()
    {
        var a = Sort(100); var b = Sort(10);
        b.Document.Descendants(Ns + "Sort").Single().SetAttributeValue("Distinct", "0");
        var beforeA = new XDocument(a.Document); var beforeB = new XDocument(b.Document);
        var model = b.IdentityModel!;
        AssertComparable(Compare(a, b).RootsB.Single());
        XNode.DeepEquals(a.Document, beforeA).Should().BeTrue();
        XNode.DeepEquals(b.Document, beforeB).Should().BeTrue();
        model.GetOperatorSource(model.Operators.First().Key).Should().NotBeNull();
    }

    [Fact]
    public void MalformedSort_IsAnEvidenceLimitationWithoutAnUnknownErrorIncident()
    {
        var a = Sort(100); var b = Sort(10);
        b.Document.Descendants(Ns + "Sort").Single().Attribute("Distinct")!.Remove();
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        var result = new PlanComparisonController(unexpectedErrors: reporter).BuildComparison(a, b, Ns);
        AssertNoDeltas(result.Statements.Single().RootsB.Single());
        reporter.Operations.Should().BeEmpty();
    }

    private static void AssertUnknownObject(StatementComparisonResult pair)
    {
        pair.Conditions.EstimatesComparable.Should().BeFalse();
        pair.Conditions.RuntimeComparable.Should().BeFalse();
        pair.Conditions.Reasons.Should().Contain(r => r.Contains("对象身份不完整", StringComparison.Ordinal));
        AssertNoDeltas(pair.RootsB.Single());
        pair.RootsB.Single().Cost.Should().Be(1);
        pair.RootsB.Single().RuntimeDeltas.Single(d => d.Label == "Elapsed").Value.Should().Be(10);
    }

    private static void AssertNoDeltas(PlanComparisonNode node)
    {
        node.Confidence.Should().NotBe(ComparisonConfidence.High);
        node.CostDelta.Should().BeNull(); node.CostPercentDelta.Should().BeNull();
        node.RuntimeDeltas.Should().OnlyContain(d => d.Delta == null && d.PercentDelta == null);
    }

    private static void AssertComparable(PlanComparisonNode node)
    {
        node.Confidence.Should().Be(ComparisonConfidence.High);
        node.CostDelta.Should().Be(0);
        node.RuntimeDeltas.Single(d => d.Label == "Elapsed").Delta.Should().Be(-90);
    }

    private static StatementComparisonResult Compare(PlanSnapshot a, PlanSnapshot b) => PlanComparisonMultiStatementTests.Compare(a, b).Statements.Single();
    private static PlanSnapshot Scan(int elapsed, string table = "T") => PlanComparisonMultiStatementTests.Snapshot(
        PlanComparisonMultiStatementTests.Statement("SELECT K, J FROM dbo.T", 1, table, $"ActualElapsedms='{elapsed}' ActualLogicalReads='20' ActualRowsRead='2'"));
    private static PlanSnapshot Constant(int elapsed)
    {
        var snapshot = Scan(elapsed);
        var root = snapshot.Document.Descendants(Ns + "RelOp").Single();
        root.SetAttributeValue("PhysicalOp", "Constant Scan"); root.SetAttributeValue("LogicalOp", "Constant Scan");
        root.Element(Ns + "TableScan")!.ReplaceWith(new XElement(Ns + "ConstantScan"));
        snapshot.Document.Descendants(Ns + "StmtSimple").Single().SetAttributeValue("StatementText", "SELECT 1");
        return snapshot;
    }

    private static PlanSnapshot Hash(int elapsed)
    {
        var snapshot = Scan(elapsed);
        var root = snapshot.Document.Descendants(Ns + "RelOp").Single();
        root.SetAttributeValue("PhysicalOp", "Hash Match"); root.SetAttributeValue("LogicalOp", "Inner Join");
        root.Element(Ns + "TableScan")!.Remove();
        root.Add(SafeXmlHelper.ParseSafe($"""
            <Hash xmlns="{Ns}"><BuildResidual><ScalarOperator ScalarString="[T].[K]=(1)"><Compare CompareOp="EQ">
              <ScalarOperator><Identifier><ColumnReference Database="[db]" Schema="[dbo]" Table="[T]" Column="K"/></Identifier></ScalarOperator>
              <ScalarOperator ScalarString="(1)"><Const ConstValue="(1)"/></ScalarOperator>
            </Compare></ScalarOperator></BuildResidual></Hash>
            """).Root!);
        root.Element(Ns + "Hash")!.Add(new XElement(Scan(elapsed).Document.Descendants(Ns + "RelOp").Single()),
            new XElement(Scan(elapsed, "U").Document.Descendants(Ns + "RelOp").Single()));
        return snapshot;
    }

    private static PlanSnapshot Sort(int elapsed, bool top = false)
    {
        var snapshot = Scan(elapsed);
        var root = snapshot.Document.Descendants(Ns + "RelOp").Single();
        var child = new XElement(root);
        root.SetAttributeValue("PhysicalOp", "Sort"); root.SetAttributeValue("LogicalOp", top ? "TopN Sort" : "Sort");
        root.Element(Ns + "TableScan")!.Remove();
        var sort = SafeXmlHelper.ParseSafe($"""
            <Sort xmlns="{Ns}" Distinct="false"><OrderBy>
              <OrderByColumn Ascending="true"><ColumnReference Database="[db]" Schema="[dbo]" Table="[T]" Column="K"/></OrderByColumn>
              <OrderByColumn Ascending="true"><ColumnReference Database="[db]" Schema="[dbo]" Table="[T]" Column="J"/></OrderByColumn>
            </OrderBy></Sort>
            """).Root!;
        if (top) { sort.Name = Ns + "TopSort"; sort.SetAttributeValue("Rows", "10"); sort.SetAttributeValue("WithTies", "false"); }
        sort.Add(child); root.Add(sort);
        return snapshot;
    }
}
