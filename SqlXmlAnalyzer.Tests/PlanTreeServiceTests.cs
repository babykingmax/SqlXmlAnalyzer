using System;
using System.Linq;
using System.Windows.Media;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanTreeServiceTests
    {
        private static readonly XNamespace ShowplanNs =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void BuildVisualTree_BuildsRootAndDirectChildren()
        {
            var service = new PlanTreeService();
            XDocument document = CreatePlanDocument(
                new XElement(
                    ShowplanNs + "RelOp",
                    new XAttribute("PhysicalOp", "Nested Loops"),
                    new XAttribute("LogicalOp", "Inner Join"),
                    new XAttribute("EstimatedTotalSubtreeCost", "12.5"),
                    new XAttribute("EstimateRows", "42"),
                    new XElement(
                        ShowplanNs + "NestedLoops",
                        new XElement(
                            ShowplanNs + "RelOp",
                            new XAttribute("PhysicalOp", "Index Seek"),
                            new XAttribute("EstimatedTotalSubtreeCost", "1.2")))));

            PlanVisualNode[] nodes = service.BuildVisualTree(document, ShowplanNs).ToArray();

            nodes.Should().ContainSingle();
            nodes[0].PhysicalOp.Should().Be("Nested Loops");
            nodes[0].LogicalOp.Should().Be("Inner Join");
            nodes[0].Cost.Should().Be(12.5);
            nodes[0].EstRows.Should().Be("42");
            nodes[0].CostColor.Should().BeSameAs(Brushes.Red);
            nodes[0].Children.Should().ContainSingle();
            nodes[0].Children[0].PhysicalOp.Should().Be("Index Seek");
        }

        [Fact]
        public void BuildOperatorTree_BuildsHeadersAndSourceTags()
        {
            var service = new PlanTreeService();
            XElement rootRelOp = new(
                ShowplanNs + "RelOp",
                new XAttribute("PhysicalOp", "Hash Match"),
                new XAttribute("EstimatedTotalSubtreeCost", "7.25"),
                new XElement(
                    ShowplanNs + "Hash",
                    new XElement(
                        ShowplanNs + "RelOp",
                        new XAttribute("PhysicalOp", "Table Scan"),
                        new XAttribute("EstimatedTotalSubtreeCost", "4.0"))));
            XDocument document = CreatePlanDocument(rootRelOp);

            PlanOperatorTreeNode? root = service.BuildOperatorTree(document, ShowplanNs);

            root.Should().NotBeNull();
            root!.Header.Should().Be("Hash Match (Cost: 7.25)");
            root.Source.Should().BeSameAs(rootRelOp);
            root.Children.Should().ContainSingle();
            root.Children[0].Header.Should().Be("Table Scan (Cost: 4.0)");
        }

        [Fact]
        public void BuildVisualTree_WhenNoRelOp_ReturnsEmptyCollection()
        {
            var service = new PlanTreeService();
            XDocument document = CreatePlanDocument(new XElement(ShowplanNs + "MissingRelOp"));

            service.BuildVisualTree(document, ShowplanNs).Should().BeEmpty();
        }

        [Fact]
        public void BuildOperatorTree_WhenNoRelOp_ReturnsNull()
        {
            var service = new PlanTreeService();
            XDocument document = CreatePlanDocument(new XElement(ShowplanNs + "MissingRelOp"));

            service.BuildOperatorTree(document, ShowplanNs).Should().BeNull();
        }

        [Theory]
        [InlineData("5.0", "Black")]
        [InlineData("5.1", "DarkOrange")]
        [InlineData("10.1", "Red")]
        public void BuildVisualTree_AssignsCostSeverityBrush(
            string cost,
            string expectedBrush)
        {
            var service = new PlanTreeService();
            XDocument document = CreatePlanDocument(
                new XElement(
                    ShowplanNs + "RelOp",
                    new XAttribute("PhysicalOp", "Sort"),
                    new XAttribute("EstimatedTotalSubtreeCost", cost)));

            PlanVisualNode node = service.BuildVisualTree(document, ShowplanNs).Single();

            node.CostColor.Should().BeSameAs(expectedBrush switch
            {
                "Red" => Brushes.Red,
                "DarkOrange" => Brushes.DarkOrange,
                _ => Brushes.Black
            });
        }

        [Fact]
        public void BuildVisualTree_WhenDocumentIsNull_Throws()
        {
            var service = new PlanTreeService();

            Action act = () => service.BuildVisualTree(null!, ShowplanNs);

            act.Should().Throw<ArgumentNullException>();
        }

        private static XDocument CreatePlanDocument(XElement relOp)
        {
            return new XDocument(
                new XElement(
                    ShowplanNs + "ShowPlanXML",
                    new XElement(
                        ShowplanNs + "BatchSequence",
                        relOp)));
        }
    }
}
