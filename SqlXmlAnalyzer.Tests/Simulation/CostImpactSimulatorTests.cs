using System.Collections.Generic;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Simulation;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Simulation
{
    public class CostImpactSimulatorTests
    {
        private readonly XNamespace _ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void Simulate_WithValidScan_ShouldReturnPositiveReduction()
        {
            // Arrange
            string xml = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                             <BatchSequence><Batch><Statements><StmtSimple StatementSubTreeCost=""10.0"">
                                <QueryPlan><RelOp PhysicalOp=""Table Scan"" EstimatedTotalSubtreeCost=""6.0"">
                                    <TableScan>
                                        <Object Table=""[Orders]"" />
                                    </TableScan>
                                </RelOp></QueryPlan>
                             </StmtSimple></Statements></Batch></BatchSequence>
                           </ShowPlanXML>";
            var planDoc = XDocument.Parse(xml);

            var suggestion = new MissingIndexSuggestion
            {
                Table = "[Orders]",
                KeyColumns = new List<IndexColumn> { new IndexColumn { Name = "[Id]", Usage = "EQUALITY" } }
            };

            // Act
            var result = CostImpactSimulator.Simulate(planDoc, suggestion, _ns);

            // Assert
            // 6.0 / 10.0 = 0.6. Reduction is 0.6 * 0.6 = 0.36 = 36%
            result.ReductionPercent.Should().Be(36);
            result.Description.Should().Contain("新索引可优化 1 个操作符");
        }

        [Fact]
        public void Simulate_WithUnrelatedTable_ShouldReturnZeroReduction()
        {
            // Arrange
            string xml = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                             <BatchSequence><Batch><Statements><StmtSimple StatementSubTreeCost=""10.0"">
                                <QueryPlan><RelOp PhysicalOp=""Table Scan"" EstimatedTotalSubtreeCost=""6.0"">
                                    <TableScan>
                                        <Object Table=""[Customers]"" />
                                    </TableScan>
                                </RelOp></QueryPlan>
                             </StmtSimple></Statements></Batch></BatchSequence>
                           </ShowPlanXML>";
            var planDoc = XDocument.Parse(xml);

            var suggestion = new MissingIndexSuggestion
            {
                Table = "[Orders]",
                KeyColumns = new List<IndexColumn> { new IndexColumn { Name = "[Id]", Usage = "EQUALITY" } }
            };

            // Act
            var result = CostImpactSimulator.Simulate(planDoc, suggestion, _ns);

            // Assert
            result.ReductionPercent.Should().Be(0);
            result.Description.Should().Contain("影响较小");
        }

        [Fact]
        public void Simulate_WithNullPlan_ShouldReturnZero()
        {
            // Arrange
            var suggestion = new MissingIndexSuggestion
            {
                Table = "[Orders]",
                KeyColumns = new List<IndexColumn> { new IndexColumn { Name = "[Id]", Usage = "EQUALITY" } }
            };

            // Act
            var result = CostImpactSimulator.Simulate(null, suggestion, _ns);

            // Assert
            result.ReductionPercent.Should().Be(0);
        }
    }
}
