using System;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanPropertyServiceTests
    {
        private static readonly XNamespace ShowplanNs =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void BuildProperties_TranslatesCoreRelOpAttributes()
        {
            var service = new PlanPropertyService();
            var relOp = new XElement(
                ShowplanNs + "RelOp",
                new XAttribute("NodeId", "7"),
                new XAttribute("PhysicalOp", "Index Seek"),
                new XAttribute("LogicalOp", "Index Seek"),
                new XAttribute("EstimateRows", "42"),
                new XAttribute("EstimatedTotalSubtreeCost", "3.14"));

            PlanPropertyItem[] properties = service.BuildProperties(relOp).ToArray();

            properties.Should().Contain(new PlanPropertyItem("Operator", "Node ID", "7"));
            properties.Should().Contain(new PlanPropertyItem("Operator", "Physical Operator", "Index Seek"));
            properties.Should().Contain(new PlanPropertyItem("Estimates", "Estimated Rows", "42"));
            properties.Should().Contain(new PlanPropertyItem("Estimates", "Estimated Subtree Cost", "3.14"));
        }

        [Fact]
        public void BuildProperties_AddsOutputListColumns()
        {
            var service = new PlanPropertyService();
            var relOp = new XElement(
                ShowplanNs + "RelOp",
                new XElement(
                    ShowplanNs + "OutputList",
                    new XElement(
                        ShowplanNs + "ColumnReference",
                        new XAttribute("Database", "[Sales]"),
                        new XAttribute("Schema", "[dbo]"),
                        new XAttribute("Table", "[Orders]"),
                        new XAttribute("Column", "[OrderId]"))));

            PlanPropertyItem[] properties = service.BuildProperties(relOp).ToArray();

            properties.Should().Contain(new PlanPropertyItem(
                "Output List",
                "[Sales].[dbo].[Orders].[OrderId]",
                string.Empty));
        }

        [Fact]
        public void BuildProperties_AddsRuntimeCounterMetrics()
        {
            var service = new PlanPropertyService();
            var relOp = new XElement(
                ShowplanNs + "RelOp",
                new XElement(
                    ShowplanNs + "RunTimeInformation",
                    new XElement(
                        ShowplanNs + "RunTimeCountersPerThread",
                        new XAttribute("ActualRows", "100"),
                        new XAttribute("ActualLogicalReads", "250"))));

            PlanPropertyItem[] properties = service.BuildProperties(relOp).ToArray();

            properties.Should().Contain(new PlanPropertyItem("Runtime", "Actual Rows", "100"));
            properties.Should().Contain(new PlanPropertyItem("Runtime", "Actual Logical Reads", "250"));
        }

        [Fact]
        public void BuildProperties_IgnoresNestedRelOpChildren()
        {
            var service = new PlanPropertyService();
            var relOp = new XElement(
                ShowplanNs + "RelOp",
                new XElement(
                    ShowplanNs + "RelOp",
                    new XAttribute("NodeId", "child")));

            PlanPropertyItem[] properties = service.BuildProperties(relOp).ToArray();

            properties.Should().NotContain(property =>
                property.Name == "Node ID" &&
                property.Value == "child");
        }

        [Fact]
        public void BuildProperties_WhenRelOpIsNull_Throws()
        {
            var service = new PlanPropertyService();

            Action act = () => service.BuildProperties(null!);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
