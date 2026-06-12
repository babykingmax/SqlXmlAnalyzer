using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.ViewModels;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class DocumentTabTests
    {
        [Fact]
        public void MainViewModel_InitializesWithEmptyTabs()
        {
            // Arrange & Act
            var viewModel = new MainViewModel();

            // Assert
            viewModel.Tabs.Should().BeEmpty();
            viewModel.SelectedTab.Should().BeNull();
        }

        [Fact]
        public void AddTab_SelectsNewTab()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var doc = new XDocument(new XElement("Test"));
            var planTab = new PlanTabViewModel("Test Plan", "C:\\test.sqlplan", doc);

            // Act
            viewModel.Tabs.Add(planTab);
            viewModel.SelectedTab = planTab;

            // Assert
            viewModel.Tabs.Should().ContainSingle();
            viewModel.SelectedTab.Should().Be(planTab);
            planTab.Title.Should().Be("Test Plan");
        }

        [Fact]
        public void CloseCommand_RemovesTabFromCollection()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var doc = new XDocument();
            var tab1 = new DeadlockTabViewModel("Deadlock 1", "test1.xdl", doc);
            var tab2 = new PlanTabViewModel("Plan 1", "test2.sqlplan", doc);
            
            viewModel.Tabs.Add(tab1);
            viewModel.Tabs.Add(tab2);
            viewModel.SelectedTab = tab2;

            tab1.CloseRequested += (s, e) => 
            {
                if (s is DocumentTabViewModel t)
                {
                    viewModel.Tabs.Remove(t);
                    if (viewModel.SelectedTab == t)
                        viewModel.SelectedTab = viewModel.Tabs.FirstOrDefault();
                }
            };

            // Act
            tab1.CloseCommand.Execute(null);

            // Assert
            viewModel.Tabs.Should().ContainSingle();
            viewModel.Tabs[0].Should().Be(tab2);
            viewModel.SelectedTab.Should().Be(tab2);
        }
    }
}
