using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphRelOpDetailsServiceTests
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        private readonly PlanGraphRelOpDetailsService _service = new();

        [Fact]
        public void Parse_WhenObjectHasAlias_ReturnsObjectIdentity()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}" PhysicalOp="Index Seek">
                  <IndexScan>
                    <Object Database="[SalesDb]" Table="[Orders]" Index="[IX_Orders]" Alias="[o]" />
                  </IndexScan>
                </RelOp>
                """);

            PlanGraphRelOpDetails result = _service.Parse(relOp, Ns, "Index Seek");

            result.DatabaseName.Should().Be("SalesDb");
            result.TableName.Should().Be("Orders");
            result.IndexName.Should().Be("IX_Orders");
            result.ObjectDetails.Should().Be("[Orders].[IX_Orders] AS [o]");
        }

        [Fact]
        public void Parse_WhenScanHasNoObject_ReturnsHeapFallbackText()
        {
            XElement relOp = new(Ns + "RelOp");

            PlanGraphRelOpDetails result = _service.Parse(relOp, Ns, "Table Scan");

            result.ObjectDetails.Should().Be("(堆表或堆索引)");
        }

        [Fact]
        public void Parse_WhenOutputListHasDuplicates_ReturnsUniqueColumns()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}">
                  <OutputList>
                    <ColumnReference Column="OrderId" />
                    <ColumnReference Column="OrderId" />
                    <ColumnReference Column="Status" />
                  </OutputList>
                </RelOp>
                """);

            PlanGraphRelOpDetails result = _service.Parse(relOp, Ns, "Index Scan");

            result.OutputColumns.Should().Equal("OrderId", "Status");
        }

        [Fact]
        public void Parse_ReturnsPredicateAndSeekPredicateScalars()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}">
                  <IndexScan>
                    <SeekPredicates>
                      <SeekPredicateNew>
                        <ScalarOperator ScalarString="[Orders].[CustomerId]=@p0" />
                      </SeekPredicateNew>
                    </SeekPredicates>
                    <Predicate>
                      <ScalarOperator ScalarString="[Orders].[Status]='Open'" />
                    </Predicate>
                  </IndexScan>
                </RelOp>
                """);

            PlanGraphRelOpDetails result = _service.Parse(relOp, Ns, "Index Seek");

            result.SeekPredicates.Should().Equal("[Orders].[CustomerId]=@p0");
            result.Predicates.Should().Equal("[Orders].[Status]='Open'");
        }

        [Fact]
        public void Parse_WhenPartitionRangeHasStartAndEnd_ReturnsRange()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}">
                  <IndexScan>
                    <PartitionsAccessed PartitionCount="8">
                      <PartitionRange Start="1" End="8" />
                    </PartitionsAccessed>
                  </IndexScan>
                </RelOp>
                """);

            PlanGraphRelOpDetails result = _service.Parse(relOp, Ns, "Index Scan");

            result.IsPartitioned.Should().BeTrue();
            result.PartitionCount.Should().Be("8");
            result.PartitionRange.Should().Be("1 - 8");
        }

        [Fact]
        public void Parse_WhenPartitionRangeHasOnlyStart_ReturnsStart()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}">
                  <IndexScan>
                    <PartitionsAccessed PartitionCount="8">
                      <PartitionRange Start="3" />
                    </PartitionsAccessed>
                  </IndexScan>
                </RelOp>
                """);

            PlanGraphRelOpDetails result = _service.Parse(relOp, Ns, "Index Scan");

            result.IsPartitioned.Should().BeTrue();
            result.PartitionCount.Should().Be("8");
            result.PartitionRange.Should().Be("3");
        }

        [Theory]
        [InlineData("true")]
        [InlineData("True")]
        [InlineData("1")]
        public void Parse_WhenPartitionedAttributeExists_ReturnsPartitioned(
            string partitioned)
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}">
                  <IndexScan Partitioned="{partitioned}" />
                </RelOp>
                """);

            PlanGraphRelOpDetails result = _service.Parse(relOp, Ns, "Index Scan");

            result.IsPartitioned.Should().BeTrue();
        }

        [Fact]
        public void Parse_IgnoresWarningsRuntimeInformationAndNestedRelOpBranches()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}">
                  <Warnings>
                    <Predicate>
                      <ScalarOperator ScalarString="[Ignored]=1" />
                    </Predicate>
                  </Warnings>
                  <RunTimeInformation>
                    <Predicate>
                      <ScalarOperator ScalarString="[AlsoIgnored]=1" />
                    </Predicate>
                  </RunTimeInformation>
                  <RelOp>
                    <Predicate>
                      <ScalarOperator ScalarString="[NestedIgnored]=1" />
                    </Predicate>
                  </RelOp>
                </RelOp>
                """);

            PlanGraphRelOpDetails result = _service.Parse(relOp, Ns, "Index Seek");

            result.Predicates.Should().BeEmpty();
            result.SeekPredicates.Should().BeEmpty();
        }
    }
}
