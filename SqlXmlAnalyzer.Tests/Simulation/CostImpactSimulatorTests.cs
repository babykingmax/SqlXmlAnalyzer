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
        public void Simulate_WithUnboundScan_ShouldKeepBenefitUnknown()
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
            result.ReductionPercent.Should().BeNull();
            result.TotalOwnCost.Value.Should().Be(6);
            result.RelatedOwnCost.Value.Should().BeNull();
            result.EvidenceCode.Should().Be("SIMULATION_TARGET_UNRESOLVED");
        }

        [Fact]
        public void Simulate_WithUnrelatedTable_ShouldNotClaimZeroBenefit()
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
            result.ReductionPercent.Should().BeNull();
            result.RelatedOwnCost.Value.Should().BeNull();
            result.EvidenceCode.Should().Be("SIMULATION_TARGET_UNRESOLVED");
        }

        [Fact]
        public void Simulate_WithNullPlan_ShouldReturnMissingEvidence()
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
            result.ReductionPercent.Should().BeNull();
            result.TotalOwnCost.Value.Should().BeNull();
            result.EvidenceCode.Should().Be("SIMULATION_INPUT_MISSING");
        }
    }
}
