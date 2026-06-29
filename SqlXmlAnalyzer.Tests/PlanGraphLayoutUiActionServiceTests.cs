using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphLayoutUiActionServiceTests
    {
        [Fact]
        public void ApplyLayeredLayout_WhenHorizontal_UpdatesNodePositions()
        {
            var service = new PlanGraphLayoutUiActionService();
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            Dictionary<XElement, PlanNodeViewModel> nodeMap =
                relOps.ToDictionary(
                    relOp => relOp,
                    relOp => new PlanNodeViewModel { RawElement = relOp });

            service.ApplyLayeredLayout(
                relOps,
                XNamespace.None,
                nodeMap,
                PlanLayoutMode.Horizontal);

            nodeMap[FindByNodeId(relOps, "0")].Location.X.Should().Be(50);
            nodeMap[FindByNodeId(relOps, "1")].Location.X.Should().Be(330);
        }

        [Fact]
        public void ReapplyLayout_WhenVertical_UpdatesConnectionLayoutMode()
        {
            var service = new PlanGraphLayoutUiActionService();
            XDocument document = CreateThreeNodePlan();
            List<XElement> relOps = document.Descendants("RelOp").ToList();
            PlanNodeViewModel root = new()
            {
                RawElement = FindByNodeId(relOps, "0")
            };
            PlanNodeViewModel child = new()
            {
                RawElement = FindByNodeId(relOps, "1")
            };
            var connection = new ConnectionViewModel
            {
                Source = child,
                Target = root,
                LayoutMode = PlanLayoutMode.Horizontal
            };

            service.ReapplyLayout(
                document,
                XNamespace.None,
                [root, child],
                [connection],
                PlanLayoutMode.Vertical);

            connection.LayoutMode.Should().Be(PlanLayoutMode.Vertical);
            child.Location.Y.Should().BeGreaterThan(root.Location.Y);
        }

        [Fact]
        public void ReapplyLayout_WhenDocumentIsMissing_DoesNotChangeConnectionLayout()
        {
            var service = new PlanGraphLayoutUiActionService();
            var connection = new ConnectionViewModel
            {
                LayoutMode = PlanLayoutMode.Horizontal
            };

            service.ReapplyLayout(
                null,
                XNamespace.None,
                [],
                [connection],
                PlanLayoutMode.Vertical);

            connection.LayoutMode.Should().Be(PlanLayoutMode.Horizontal);
        }

        private static XElement FindByNodeId(
            IEnumerable<XElement> relOps,
            string nodeId)
        {
            return relOps.Single(relOp =>
                relOp.Attribute("NodeId")?.Value == nodeId);
        }

        private static XDocument CreateThreeNodePlan()
        {
            return new XDocument(
                new XElement("ShowPlan",
                    new XElement("RelOp",
                        new XAttribute("NodeId", "0"),
                        new XAttribute("EstimatedTotalSubtreeCost", "10"),
                        new XElement("RelOp",
                            new XAttribute("NodeId", "1"),
                            new XAttribute("EstimatedTotalSubtreeCost", "4")),
                        new XElement("RelOp",
                            new XAttribute("NodeId", "2"),
                            new XAttribute("EstimatedTotalSubtreeCost", "6")))));
        }
    }
}
