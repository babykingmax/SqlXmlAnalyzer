using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Comparison;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanComparisonPredicateIdentityTests
{
    private static readonly XNamespace Ns = PlanComparisonMultiStatementTests.Ns;

    [Fact]
    public void DifferentSeekColumnsWithSameConstant_DoNotProduceComparableOperatorDeltas()
    {
        var result = PlanComparisonMultiStatementTests.Compare(Seek("K", "GT", 100), Seek("J", "GT", 10));
        var node = result.Statements.Single().RootsB.Single();
        node.Confidence.Should().NotBe(ComparisonConfidence.High);
        node.CostDelta.Should().BeNull();
        node.RuntimeDeltas.Should().OnlyContain(d => d.Delta == null);
    }

    internal static PlanSnapshot Seek(string column = "K", string scanType = "GT", int elapsed = 100)
    {
        var snapshot = PlanComparisonMultiStatementTests.RuntimeSnapshot(elapsed);
        var statement = snapshot.Document.Descendants(Ns + "StmtSimple").Single();
        statement.SetAttributeValue("StatementText", "SELECT * FROM dbo.T WHERE K > 1 AND J > 1");
        var op = snapshot.Document.Descendants(Ns + "RelOp").Single();
        op.SetAttributeValue("PhysicalOp", "Index Seek"); op.SetAttributeValue("LogicalOp", "Index Seek");
        op.Element(Ns + "ConstantScan")!.Remove();
        op.Add(SafeXmlHelper.ParseSafe($"""
            <IndexScan xmlns="{Ns}">
              <Object Database="[db]" Schema="[dbo]" Table="[T]" Alias="[T]" Index="[IX_{column}]"/>
              <SeekPredicates><SeekPredicateNew><SeekKeys><StartRange ScanType="{scanType}">
                <RangeColumns><ColumnReference Database="[db]" Schema="[dbo]" Table="[T]" Alias="[T]" Column="{column}"/></RangeColumns>
                <RangeExpressions><ScalarOperator ScalarString="(1)"><Const ConstValue="(1)"/></ScalarOperator></RangeExpressions>
              </StartRange></SeekKeys></SeekPredicateNew></SeekPredicates>
            </IndexScan>
            """).Root!);
        return snapshot;
    }

    [Theory]
    [InlineData("ScanType")]
    [InlineData("RangeColumns")]
    [InlineData("RangeExpressions")]
    [InlineData("SeekKeys")]
    [InlineData("ScalarOperator")]
    public void IncompleteMatchingSeekEvidence_IsNotHighConfidence(string missing)
    {
        var a = Seek(); var b = Seek(elapsed: 10);
        foreach (var snapshot in new[] { a, b })
        {
            if (missing == "ScanType") snapshot.Document.Descendants(Ns + "StartRange").Single().Attribute("ScanType")!.Remove();
            else
            {
                var element = snapshot.Document.Descendants(Ns + missing).Single();
                element.RemoveNodes();
                if (missing == "ScalarOperator") element.RemoveAttributes();
            }
        }
        var node = PlanComparisonMultiStatementTests.Compare(a, b).Statements.Single().RootsB.Single();
        node.Confidence.Should().NotBe(ComparisonConfidence.High);
        node.RuntimeDeltas.Should().OnlyContain(d => d.Delta == null);
    }

    [Theory]
    [InlineData("GE")]
    [InlineData("LT")]
    [InlineData("LE")]
    [InlineData("EQ")]
    public void DifferentRangeComparison_DoesNotMatch(string scanType)
    {
        var node = PlanComparisonMultiStatementTests.Compare(Seek(), Seek(scanType: scanType)).Statements.Single().RootsB.Single();
        node.Confidence.Should().NotBe(ComparisonConfidence.High);
        node.CostDelta.Should().BeNull();
    }

    [Fact]
    public void StructuredExpressionWithoutScalarString_AndAttributeOrderChanges_StillMatch()
    {
        var a = Seek(); var b = Seek(elapsed: 10);
        b.Document.Descendants(Ns + "ScalarOperator").Single().Attribute("ScalarString")!.Remove();
        var column = b.Document.Descendants(Ns + "ColumnReference").Single();
        var attributes = column.Attributes().Select(x => new XAttribute(x)).Reverse().ToArray();
        column.RemoveAttributes(); column.Add(attributes);
        var node = PlanComparisonMultiStatementTests.Compare(a, b).Statements.Single().RootsB.Single();
        node.Confidence.Should().Be(ComparisonConfidence.High);
        node.RuntimeDeltas.Single(d => d.Label == "Elapsed").Delta.Should().Be(-90);
    }

    [Fact]
    public void CompositeKeyColumnOrder_IsPartOfPredicateIdentity()
    {
        var a = Seek(); var b = Seek();
        foreach (var snapshot in new[] { a, b })
        {
            var columns = snapshot.Document.Descendants(Ns + "RangeColumns").Single();
            var second = new XElement(columns.Elements().Single()); second.SetAttributeValue("Column", "J"); columns.Add(second);
            var expressions = snapshot.Document.Descendants(Ns + "RangeExpressions").Single();
            expressions.Add(new XElement(expressions.Elements().Single()));
        }
        var reversed = b.Document.Descendants(Ns + "RangeColumns").Single();
        reversed.ReplaceNodes(reversed.Elements().Reverse().ToArray());
        PlanComparisonMultiStatementTests.Compare(a, b).Statements.Single().RootsB.Single().Confidence.Should().NotBe(ComparisonConfidence.High);
    }

    [Theory]
    [InlineData("missing-seek")]
    [InlineData("missing-constant")]
    [InlineData("unknown-expression")]
    [InlineData("foreign-expression")]
    [InlineData("empty-seek-sibling")]
    public void UnknownOrIncompleteTypedPredicate_CannotEstablishHighConfidence(string damage)
    {
        var a = Seek(); var b = Seek(elapsed: 10);
        foreach (var snapshot in new[] { a, b })
        {
            var constant = snapshot.Document.Descendants(Ns + "Const").Single();
            switch (damage)
            {
                case "missing-seek": snapshot.Document.Descendants(Ns + "SeekPredicates").Remove(); break;
                case "missing-constant": constant.Attribute("ConstValue")!.Remove(); break;
                case "unknown-expression": constant.Name = Ns + "UnknownFutureExpression"; break;
                case "foreign-expression": constant.Name = XName.Get("Const", "urn:extension"); break;
                default: snapshot.Document.Descendants(Ns + "SeekPredicates").Single().Add(new XElement(Ns + "SeekPredicateNew")); break;
            }
        }
        var node = PlanComparisonMultiStatementTests.Compare(a, b).Statements.Single().RootsB.Single();
        node.Confidence.Should().NotBe(ComparisonConfidence.High);
        node.CostDelta.Should().BeNull();
        node.RuntimeDeltas.Should().OnlyContain(d => d.Delta == null);
    }
}
