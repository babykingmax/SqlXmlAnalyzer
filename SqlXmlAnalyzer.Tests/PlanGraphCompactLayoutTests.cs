using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanGraphCompactLayoutTests
{
    private readonly PlanGraphLayoutService _service = new();

    [Theory]
    [InlineData(100, PlanGraphLayoutDirection.Horizontal)]
    [InlineData(1000, PlanGraphLayoutDirection.Horizontal)]
    [InlineData(5000, PlanGraphLayoutDirection.Horizontal)]
    [InlineData(100, PlanGraphLayoutDirection.Vertical)]
    [InlineData(1000, PlanGraphLayoutDirection.Vertical)]
    [InlineData(5000, PlanGraphLayoutDirection.Vertical)]
    public void CalculateLayout_WhenBranchesHaveMixedDepth_PreservesOrderWithoutOverlappingCards(
        int count, PlanGraphLayoutDirection direction)
    {
        XElement root = CreateMixedTree(count);
        List<XElement> operators = root.DescendantsAndSelf().Where(element => element.Name == "RelOp").ToList();

        IReadOnlyList<PlanGraphLayoutPosition> positions = _service.CalculateLayout(
            operators, XNamespace.None, null, direction);

        positions.Should().HaveCount(count);
        positions.Select(position => position.Element).Should().Equal(operators);
        AssertSeparated(positions, direction);
        var byElement = positions.ToDictionary(position => position.Element);
        foreach (XElement parent in operators)
        {
            var children = PlanDiagnosticAnalyzer.GetDirectChildRelOps(parent, XNamespace.None);
            for (int index = 1; index < children.Count; index++)
            {
                Cross(byElement[children[index]], direction).Should()
                    .BeGreaterThan(Cross(byElement[children[index - 1]], direction));
            }
            foreach (XElement child in children)
            {
                Along(byElement[child], direction).Should().BeGreaterThan(Along(byElement[parent], direction));
            }
        }
    }

    [Theory]
    [InlineData(PlanGraphLayoutDirection.Horizontal)]
    [InlineData(PlanGraphLayoutDirection.Vertical)]
    public void CalculateLayout_WhenWideBranchesOccurAtDifferentDepths_ReusesUnoccupiedSpace(
        PlanGraphLayoutDirection direction)
    {
        var shallow = Operator("shallow", Enumerable.Range(0, 10).Select(index => Operator($"s{index}")).ToArray());
        var deep = Operator("deep", Enumerable.Range(0, 10).Select(index => Operator($"d{index}")).ToArray());
        for (int depth = 0; depth < 6; depth++) deep = Operator($"chain{depth}", deep);
        var root = Operator("root", shallow, deep);
        List<XElement> operators = root.DescendantsAndSelf("RelOp").ToList();

        IReadOnlyList<PlanGraphLayoutPosition> positions = _service.CalculateLayout(
            operators, XNamespace.None, null, direction);

        double occupiedCrossExtent = positions.Max(position => Cross(position, direction))
            - positions.Min(position => Cross(position, direction));
        double cardExtent = direction == PlanGraphLayoutDirection.Horizontal
            ? PlanGraphNodeMetrics.Height : PlanGraphNodeMetrics.Width;
        // The previous leaf-reservation algorithm needs 19 whole slots even
        // using today's smaller spacing. Contour packing must save real space.
        double leafReservedExtent = 19 * (cardExtent + 24);
        occupiedCrossExtent.Should().BeLessThan(leafReservedExtent * 0.85);
        AssertSeparated(positions, direction);
    }

    [Fact]
    public void CalculateLayout_WhenFiveThousandOperatorsFormAChain_DoesNotRequireRecursiveLayout()
    {
        var operators = Enumerable.Range(0, 5000).Select(index => Operator(index.ToString())).ToList();
        for (int index = operators.Count - 2; index >= 0; index--)
            operators[index].Add(operators[index + 1]);

        IReadOnlyList<PlanGraphLayoutPosition> positions = _service.CalculateLayout(
            operators, XNamespace.None, null, PlanGraphLayoutDirection.Horizontal);

        positions.Should().HaveCount(5000);
        positions.Select(position => position.Y).Distinct().Should().ContainSingle().Which.Should().Be(50);
        positions[^1].X.Should().Be(50 + 4999 * (PlanGraphNodeMetrics.Width + 72));
    }

    [Fact]
    public void CalculateLayout_WhenIntermediateOperatorIsOutsidePage_DoesNotLinkAcrossMissingInput()
    {
        var leaf = Operator("leaf");
        var missing = Operator("missing", leaf);
        var root = Operator("root", missing);

        IReadOnlyList<PlanGraphLayoutPosition> positions = _service.CalculateLayout(
            [root, leaf], XNamespace.None, null, PlanGraphLayoutDirection.Horizontal);

        positions.Should().HaveCount(2);
        positions.Select(position => position.X).Should().OnlyContain(x => x == 50);
        AssertSeparated(positions, PlanGraphLayoutDirection.Horizontal);
    }

    [Fact]
    public void CalculateLayout_WhenNestedBranchIsCollapsed_RemovesOnlyItsDescendantsAndReclaimsSpace()
    {
        var branch = Operator("branch", Enumerable.Range(0, 12).Select(index => Operator(index.ToString())).ToArray());
        var sibling = Operator("sibling");
        var root = Operator("root", branch, sibling);
        List<XElement> operators = root.DescendantsAndSelf("RelOp").ToList();

        IReadOnlyList<PlanGraphLayoutPosition> expanded = _service.CalculateLayout(
            operators, XNamespace.None, null, PlanGraphLayoutDirection.Horizontal);
        IReadOnlyList<PlanGraphLayoutPosition> collapsed = _service.CalculateLayout(
            operators, XNamespace.None, new HashSet<XElement> { branch }, PlanGraphLayoutDirection.Horizontal);

        collapsed.Select(position => position.Element).Should().Equal(root, branch, sibling);
        collapsed.Max(position => position.Y).Should().BeLessThan(expanded.Max(position => position.Y));
        AssertSeparated(collapsed, PlanGraphLayoutDirection.Horizontal);
    }

    [Fact]
    public void CalculateLayout_WhenInputCollectionOrderDiffers_KeepsXmlInputOrder()
    {
        var first = Operator("outer");
        var second = Operator("inner");
        var root = Operator("join", first, second);

        IReadOnlyList<PlanGraphLayoutPosition> positions = _service.CalculateLayout(
            [second, root, first], XNamespace.None, null, PlanGraphLayoutDirection.Horizontal);

        positions.Single(position => position.Element == first).Y.Should()
            .BeLessThan(positions.Single(position => position.Element == second).Y);
    }

    private static XElement CreateMixedTree(int count)
    {
        var nodes = Enumerable.Range(0, count).Select(index => Operator(index.ToString())).ToArray();
        var random = new Random(1249);
        for (int index = 1; index < count; index++)
        {
            // Mix broad joins and long branches reproducibly; XML input order
            // remains attachment order, independently of NodeId ordering.
            int parentIndex = index % 7 == 0 ? index - 1 : random.Next(index);
            nodes[parentIndex].Element("Inputs")!.Add(nodes[index]);
        }
        return nodes[0];
    }

    private static XElement Operator(string id, params XElement[] children) =>
        new("RelOp", new XAttribute("NodeId", id), new XElement("Inputs", children));

    private static double Cross(PlanGraphLayoutPosition position, PlanGraphLayoutDirection direction) =>
        direction == PlanGraphLayoutDirection.Horizontal ? position.Y : position.X;

    private static double Along(PlanGraphLayoutPosition position, PlanGraphLayoutDirection direction) =>
        direction == PlanGraphLayoutDirection.Horizontal ? position.X : position.Y;

    private static void AssertSeparated(
        IReadOnlyList<PlanGraphLayoutPosition> positions, PlanGraphLayoutDirection direction)
    {
        double cardExtent = direction == PlanGraphLayoutDirection.Horizontal
            ? PlanGraphNodeMetrics.Height : PlanGraphNodeMetrics.Width;
        foreach (var level in positions.GroupBy(position => Along(position, direction)))
        {
            PlanGraphLayoutPosition[] ordered = level.OrderBy(position => Cross(position, direction)).ToArray();
            for (int index = 1; index < ordered.Length; index++)
            {
                (Cross(ordered[index], direction) - Cross(ordered[index - 1], direction))
                    .Should().BeGreaterThanOrEqualTo(cardExtent + 24 - 0.000001);
            }
        }
        positions.Should().OnlyContain(position => double.IsFinite(position.X)
            && double.IsFinite(position.Y) && position.X >= 50 && position.Y >= 50);
    }
}
