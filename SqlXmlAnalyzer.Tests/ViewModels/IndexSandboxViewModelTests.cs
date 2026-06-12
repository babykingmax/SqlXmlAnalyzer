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
    }
}
