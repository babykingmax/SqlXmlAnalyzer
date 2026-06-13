using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.ViewModels;
using Xunit;

namespace SqlXmlAnalyzer.Tests.ViewModels
{
    public class IndexSandboxViewModelTests
    {
        private MissingIndexSuggestion CreateTestSuggestion()
        {
            return new MissingIndexSuggestion
            {
                Schema = "[dbo]",
                Table = "[TestTable]",
                Impact = 95.0,
                KeyColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "[Col1]", Usage = "EQUALITY" },
                    new IndexColumn { Name = "[Col2]", Usage = "INEQUALITY" }
                },
                IncludeColumns = new List<IndexColumn>
                {
                    new IndexColumn { Name = "[Col3]", Usage = "INCLUDE" }
                }
            };
        }

        [Fact]
        public void Constructor_ShouldInitializeCollectionsAndScore()
        {
            // Arrange
            var suggestion = CreateTestSuggestion();

            // Act
            var vm = new IndexSandboxViewModel(suggestion);

            // Assert
            vm.KeyColumns.Should().HaveCount(2);
            vm.IncludeColumns.Should().HaveCount(1);
            vm.CurrentScore.Should().BeGreaterThan(0);
            vm.CreateIndexStatement.Should().Contain("CREATE NONCLUSTERED INDEX");
        }

        [Fact]
        public void RemoveKeyColumnCommand_ShouldRemoveAndRecalculate()
        {
            // Arrange
            var suggestion = CreateTestSuggestion();
            var vm = new IndexSandboxViewModel(suggestion);
            var colToRemove = vm.KeyColumns.First();
            int initialScore = vm.CurrentScore;

            // Act
            vm.RemoveKeyColumnCommand.Execute(colToRemove);

            // Assert
            vm.KeyColumns.Should().HaveCount(1);
            vm.KeyColumns.First().Name.Should().Be("[Col2]");
            vm.CurrentScore.Should().NotBe(0); // Score recalculates
            vm.CreateIndexStatement.Should().NotContain("[Col1]");
        }

        [Fact]
        public void MoveKeyColumnUpCommand_ShouldSwapOrderAndRecalculate()
        {
            // Arrange
            var suggestion = CreateTestSuggestion();
            var vm = new IndexSandboxViewModel(suggestion);
            var colToMove = vm.KeyColumns[1]; // [Col2]
            
            // Act
            vm.MoveKeyColumnUpCommand.Execute(colToMove);

            // Assert
            vm.KeyColumns[0].Name.Should().Be("[Col2]");
            vm.KeyColumns[1].Name.Should().Be("[Col1]");
            vm.CreateIndexStatement.Should().Contain("([Col2], [Col1])");
        }

        [Fact]
        public void MoveKeyColumnUpCommand_OnFirstItem_ShouldDoNothing()
        {
            // Arrange
            var suggestion = CreateTestSuggestion();
            var vm = new IndexSandboxViewModel(suggestion);
            var colToMove = vm.KeyColumns[0]; // [Col1]
            
            // Act
            vm.MoveKeyColumnUpCommand.Execute(colToMove);

            // Assert
            vm.KeyColumns[0].Name.Should().Be("[Col1]"); // Still first
        }

        [Fact]
        public void MoveKeyColumnDownCommand_ShouldSwapOrder()
        {
            // Arrange
            var suggestion = CreateTestSuggestion();
            var vm = new IndexSandboxViewModel(suggestion);
            var colToMove = vm.KeyColumns[0]; // [Col1]
            
            // Act
            vm.MoveKeyColumnDownCommand.Execute(colToMove);

            // Assert
            vm.KeyColumns[0].Name.Should().Be("[Col2]");
            vm.KeyColumns[1].Name.Should().Be("[Col1]");
        }
        
        [Fact]
        public void RemoveIncludeColumnCommand_ShouldRemoveAndRecalculate()
        {
            // Arrange
            var suggestion = CreateTestSuggestion();
            var vm = new IndexSandboxViewModel(suggestion);
            var colToRemove = vm.IncludeColumns.First();

            // Act
            vm.RemoveIncludeColumnCommand.Execute(colToRemove);

            // Assert
            vm.IncludeColumns.Should().BeEmpty();
            vm.CreateIndexStatement.Should().NotContain("INCLUDE");
        }

        [Fact]
        public void TippingPoint_ShouldComputeStatusCorrectly()
        {
            // Arrange
            var suggestion = CreateTestSuggestion();
            var vm = new IndexSandboxViewModel(suggestion);
            
            // Set stats manually
            vm.TotalRows = 100000;
            vm.AvgRowSize = 200;
            
            // P_table = 100000 * 200 / 8192 = 2441.4 pages
            // TippingPointLow = 2441.4 / 4 = 610
            // TippingPointHigh = 2441.4 / 3 = 814
            
            vm.TippingPointLow.Should().Be(610);
            vm.TippingPointHigh.Should().Be(814);
            
            // Act 1: Returned rows < Low (Safe)
            vm.ReturnedRows = 100;
            vm.TippingPointStatus.Should().Contain("安全");
            
            // Act 2: Returned rows between Low and High (Boundary)
            vm.ReturnedRows = 700;
            vm.TippingPointStatus.Should().Contain("临界区");
            
            // Act 3: Returned rows > High (Degraded)
            vm.ReturnedRows = 900;
            vm.TippingPointStatus.Should().Contain("已触发退化");
        }

        [Fact]
        public void IsCoveredIndex_ShouldDetectCoverageAndBypassTippingPoint()
        {
            // Arrange
            var suggestion = new MissingIndexSuggestion
            {
                Schema = "[dbo]",
                Table = "[Orders]",
                KeyColumns = new List<IndexColumn> { new IndexColumn { Name = "[Id]", Usage = "EQUALITY" } },
                IncludeColumns = new List<IndexColumn> { new IndexColumn { Name = "[Price]", Usage = "INCLUDE" } }
            };
            
            string xml = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                             <BatchSequence><Batch><Statements><StmtSimple>
                                <QueryPlan><RelOp PhysicalOp=""Table Scan"">
                                    <TableScan>
                                        <Object Table=""[Orders]"" />
                                    </TableScan>
                                    <OutputList>
                                        <ColumnReference Column=""Id"" />
                                        <ColumnReference Column=""Price"" />
                                    </OutputList>
                                </RelOp></QueryPlan>
                             </StmtSimple></Statements></Batch></BatchSequence>
                           </ShowPlanXML>";
            var planDoc = System.Xml.Linq.XDocument.Parse(xml);
            
            var vm = new IndexSandboxViewModel(suggestion, planDoc);
            
            // Act
            bool covered = vm.IsCoveredIndex;
            
            // Assert
            covered.Should().BeTrue();
            vm.TippingPointStatus.Should().Contain("覆盖索引");
        }
    }
}
