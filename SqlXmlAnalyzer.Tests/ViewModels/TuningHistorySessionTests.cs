using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.ViewModels;
using Xunit;

namespace SqlXmlAnalyzer.Tests.ViewModels
{
    public class TuningHistorySessionTests
    {
        private readonly XDocument _samplePlanXml = XDocument.Parse(@"
            <ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
                <BatchSequence>
                    <Batch>
                        <Statements>
                            <StmtSimple StatementText=""SELECT * FROM dbo.TestTable"">
                                <QueryPlan>
                                    <RelOp NodeId=""0"" EstimatedTotalSubtreeCost=""0.125"">
                                        <TableScan Table=""[TestTable]"" />
                                    </RelOp>
                                </QueryPlan>
                            </StmtSimple>
                        </Statements>
                    </Batch>
                </BatchSequence>
            </ShowPlanXML>");

        [Fact]
        public void CaptureCurrentPlan_ShouldAddSnapshotToHistory()
        {
            // Arrange
            var vm = new MainViewModel
            {
                CurrentPlanDoc = _samplePlanXml,
                CurrentPlanFilePath = "C:\\plans\\test.sqlplan"
            };

            // Act
            vm.CaptureCurrentPlan();

            // Assert
            vm.TuningHistory.Should().HaveCount(1);
            var snapshot = vm.TuningHistory.First();
            snapshot.Title.Should().Contain("test.sqlplan");
            snapshot.TotalCost.Should().Be(0.125);
            snapshot.OperatorCount.Should().Be(1);
            snapshot.StatementText.Should().Contain("SELECT * FROM dbo.TestTable");
        }

        [Fact]
        public void ComparePlans_ShouldComputeCostDeltaCorrectly()
        {
            // Arrange
            var vm = new MainViewModel();
            var snapshotA = new PlanSnapshot { TotalCost = 0.5 };
            var snapshotB = new PlanSnapshot { TotalCost = 0.2 }; // 60% optimization

            // Act
            vm.PlanA = snapshotA;
            vm.PlanB = snapshotB;

            // Assert
            vm.CompareVisible.Should().BeTrue();
            vm.CostDeltaText.Should().Contain("▼ 预计成本优化: 60.00%");
            vm.CostDeltaColor.Should().Be("#2E7D32"); // Green

            // Cost increase scenario
            vm.PlanB = new PlanSnapshot { TotalCost = 1.0 }; // 100% increase
            vm.CostDeltaText.Should().Contain("▲ 预计成本增加: 100.00%");
            vm.CostDeltaColor.Should().Be("#D32F2F"); // Red
        }

        [Fact]
        public void SaveAndLoadSession_ShouldRoundtripTuningHistory()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pesession");
            var vmSave = new MainViewModel
            {
                CurrentPlanDoc = _samplePlanXml,
                CurrentPlanFilePath = "C:\\plans\\test.sqlplan"
            };
            vmSave.CaptureCurrentPlan();
            var savedSnapshot = vmSave.TuningHistory.First();
            vmSave.PlanA = savedSnapshot;

            // Act
            vmSave.SaveSession(tempFile);

            var vmLoad = new MainViewModel();
            vmLoad.LoadSession(tempFile);

            // Assert
            File.Exists(tempFile).Should().BeTrue();
            vmLoad.TuningHistory.Should().HaveCount(1);
            
            var loadedSnapshot = vmLoad.TuningHistory.First();
            loadedSnapshot.Title.Should().Be(savedSnapshot.Title);
            loadedSnapshot.TotalCost.Should().Be(savedSnapshot.TotalCost);
            loadedSnapshot.OperatorCount.Should().Be(savedSnapshot.OperatorCount);
            loadedSnapshot.StatementText.Should().Be(savedSnapshot.StatementText);
            
            vmLoad.PlanA.Should().NotBeNull();
            vmLoad.PlanA!.Title.Should().Be(savedSnapshot.Title);

            // Cleanup
            try { File.Delete(tempFile); } catch { }
        }
    }
}
