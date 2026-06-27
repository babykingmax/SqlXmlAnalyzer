using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphNodeBuilderServiceTests
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        private readonly PlanGraphNodeBuilderService _service = new();

        [Fact]
        public void Build_MapsRelOpAttributesRuntimeCountersAndObjectDetails()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}" NodeId="5" PhysicalOp="Index Seek" LogicalOp="Index Seek"
                       EstimateRows="100" EstimatedRowsRead="250" EstimatedTotalSubtreeCost="10"
                       EstimateIO="2.5" EstimateCPU="1.25" EstimateRebinds="1" EstimateRewinds="2"
                       AvgRowSize="1024">
                  <OutputList>
                    <ColumnReference Column="OrderId" />
                  </OutputList>
                  <IndexScan>
                    <Object Database="[SalesDb]" Table="[Orders]" Index="[IX_Orders]" />
                    <SeekPredicates>
                      <SeekPredicateNew>
                        <ScalarOperator ScalarString="[Orders].[OrderId]=@p0" />
                      </SeekPredicateNew>
                    </SeekPredicates>
                    <Predicate>
                      <ScalarOperator ScalarString="[Orders].[Status]='Open'" />
                    </Predicate>
                  </IndexScan>
                  <RunTimeInformation>
                    <RunTimeCountersPerThread Thread="0" ActualRows="200" ActualRowsRead="500"
                                              ActualExecutions="3" ActualRebinds="4" ActualRewinds="5" />
                  </RunTimeInformation>
                </RelOp>
                """);

            PlanGraphNodeBuildResult result =
                _service.Build(relOp, Ns, CreateWarningSettings());

            result.RawElement.Should().BeSameAs(relOp);
            result.NodeId.Should().Be("5");
            result.PhysicalOp.Should().Be("Index Seek");
            result.LogicalOp.Should().Be("Index Seek");
            result.SubtreeCost.Should().Be(10);
            result.Cost.Should().Be(10);
            result.OwnCost.Should().Be(10);
            result.ActualRecost.Should().Be(20);
            result.EstRows.Should().Be("100");
            result.EstRowsNum.Should().Be(100);
            result.EstimatedRowsToBeRead.Should().Be("250");
            result.EstimatedCPUCostNum.Should().Be(1.25);
            result.EstimatedIOCostNum.Should().Be(2.5);
            result.AvgRowSizeNum.Should().Be(1024);
            result.EstimatedExecutions.Should().Be("4.0");
            result.ActualExecutions.Should().Be("3");
            result.ActualRows.Should().Be("200");
            result.ActualRowsRead.Should().Be("500");
            result.ActualRowsNum.Should().Be(200);
            result.EstimatedDataSize.Should().Be("100 KB");
            result.ActualDataSize.Should().Be("200 KB");
            result.ActualRebinds.Should().Be("4");
            result.ActualRewinds.Should().Be("5");
            result.DatabaseName.Should().Be("SalesDb");
            result.TableName.Should().Be("Orders");
            result.IndexName.Should().Be("IX_Orders");
            result.ObjectDetails.Should().Be("[Orders].[IX_Orders]");
            result.SeekPredicates.Should().Be("[Orders].[OrderId]=@p0");
            result.Predicate.Should().Be("[Orders].[Status]='Open'");
            result.OutputList.Should().Be("OrderId");
            result.OperatorType.Should().Be("Seek");
            result.IsParallel.Should().BeFalse();
        }

        [Fact]
        public void Build_WhenOptionalValuesAreMissing_UsesExistingDefaults()
        {
            XElement relOp = new(Ns + "RelOp",
                new XAttribute("PhysicalOp", "Parallelism"));

            PlanGraphNodeBuildResult result =
                _service.Build(relOp, Ns, CreateWarningSettings());

            result.NodeId.Should().Be("?");
            result.PhysicalOp.Should().Be("Parallelism");
            result.LogicalOp.Should().Be("Unknown");
            result.ExecutionMode.Should().Be("Row");
            result.EstRows.Should().Be("0");
            result.EstimatedRowsToBeRead.Should().Be("0");
            result.EstimatedCPUCostNum.Should().Be(0);
            result.EstimatedIOCostNum.Should().Be(0);
            result.EstimatedExecutions.Should().Be("1.0");
            result.ActualExecutions.Should().BeEmpty();
            result.ActualRows.Should().BeEmpty();
            result.ActualRowsRead.Should().BeEmpty();
            result.ActualDataSize.Should().BeEmpty();
            result.NodeSeverity.Should().Be("Info");
            result.OperatorType.Should().Be("Parallelism");
            result.IsParallel.Should().BeTrue();
        }

        [Fact]
        public void Build_WhenActualRowsCannotBeScaled_KeepsOwnCostAsActualRecost()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}" NodeId="1" PhysicalOp="Index Scan"
                       EstimateRows="0" EstimatedTotalSubtreeCost="7">
                  <RunTimeInformation>
                    <RunTimeCountersPerThread ActualRows="100" />
                  </RunTimeInformation>
                </RelOp>
                """);

            PlanGraphNodeBuildResult result =
                _service.Build(relOp, Ns, CreateWarningSettings());

            result.OwnCost.Should().Be(7);
            result.ActualRecost.Should().Be(7);
        }

        private static PlanGraphNodeWarningSettings CreateWarningSettings()
        {
            return new PlanGraphNodeWarningSettings(
                ResidualIOThreshold: 10.0,
                ResidualIOMinRowsRead: 1000);
        }
    }
}
