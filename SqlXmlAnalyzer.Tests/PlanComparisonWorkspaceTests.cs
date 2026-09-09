using FluentAssertions;
using System.Windows.Controls;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;
using SqlXmlAnalyzer.Views;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanComparisonWorkspaceTests
{
    [Fact]
    public void ManualPairAndReset_PublishAllStatementsAndClearOldResultsOnFailure()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var vm = new MainViewModel(); var treeA = new TreeView(); var treeB = new TreeView();
            var ui = new PlanComparisonUiActionService(new PlanComparisonController(), new PlanComparisonTreeService(),
                new PlanComparisonTreeViewRenderer(), vm, new TabControl(), treeA, treeB, PlanComparisonMultiStatementTests.Ns);
            vm.PropertyChanged += ui.HandleViewModelPropertyChanged;
            vm.PlanA = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 1) + PlanComparisonMultiStatementTests.Statement("SELECT 2", 2));
            vm.PlanB = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 1) + PlanComparisonMultiStatementTests.Statement("SELECT 2", 999));
            treeA.Items.Cast<object>().Should().HaveCount(2); treeB.Items.Cast<object>().Should().HaveCount(2);
            vm.ComparisonSummary.Should().Contain("配对 2"); vm.CostDeltaText.Should().Contain("N/A");
            vm.ManualScopeA = vm.ComparisonScopesA[1]; vm.ManualScopeB = vm.ComparisonScopesB[1];
            vm.ApplyComparisonSelectionCommand.Execute(null);
            treeA.Items.Cast<object>().Should().ContainSingle();
            vm.Comparison!.Statements.Single().Confidence.Should().Be(Core.Comparison.ComparisonConfidence.Manual);
            vm.Comparison.Statements.Single().RootsB.Single().Cost.Should().Be(999);
            vm.ManualScopeA = null; vm.ManualScopeB = null;
            ui.RefreshCompareTrees();
            vm.ManualScopeA!.Key.Should().Be(vm.ComparisonSelection.A);
            vm.ManualScopeB!.Key.Should().Be(vm.ComparisonSelection.B);
            vm.ResetComparisonSelectionCommand.Execute(null); treeA.Items.Cast<object>().Should().HaveCount(2);
            vm.ManualScopeA = vm.ComparisonScopesA[0]; vm.ManualScopeB = vm.ComparisonScopesB[0];
            vm.ApplyComparisonSelectionCommand.Execute(null);
            vm.PlanA.Document.Root!.SetAttributeValue("Build", "17.0.1.1"); ui.RefreshCompareTrees();
            vm.Comparison.Should().BeNull(); vm.ComparisonSummary.Should().Contain("PLAN_SELECTION_NOT_FOUND");
            treeA.Items.OfType<TreeViewItem>().Should().BeEmpty();
            vm.ResetComparisonSelectionCommand.Execute(null); vm.Comparison.Should().NotBeNull();
            vm.ClearResults(); treeA.Items.Cast<object>().Should().BeEmpty(); treeB.Items.Cast<object>().Should().BeEmpty();
        });
    }

    [Fact]
    public void ProductionViewBindsManualSelectionSummaryAndConditions()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "SqlXmlAnalyzer.sln"))) directory = directory.Parent;
            var source = SafeXmlHelper.LoadSafe(System.IO.Path.Combine(directory!.FullName, "Views/PlanComparisonWorkspaceView.xaml")).Root!;
            System.Windows.FrameworkElement Load(string name)
            {
                var element = new System.Xml.Linq.XElement(source.Descendants().Single(e =>
                    (string?)e.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name));
                foreach (var declaration in source.Attributes().Where(a => a.IsNamespaceDeclaration)) element.Add(new System.Xml.Linq.XAttribute(declaration));
                using var reader = element.CreateReader();
                return (System.Windows.FrameworkElement)System.Windows.Markup.XamlReader.Load(reader);
            }
            foreach (string name in new[] { "ComparisonMatchSummary", "ComparisonConditionsText" })
                System.Windows.Data.BindingOperations.GetBinding((TextBlock)Load(name), TextBlock.TextProperty).Should().NotBeNull();
            foreach (string name in new[] { "ComparisonScopeA", "ComparisonScopeB" })
            {
                var combo = (ComboBox)Load(name);
                System.Windows.Data.BindingOperations.GetBinding(combo, ItemsControl.ItemsSourceProperty).Should().NotBeNull();
                System.Windows.Data.BindingOperations.GetBinding(combo, System.Windows.Controls.Primitives.Selector.SelectedItemProperty).Should().NotBeNull();
            }
        });
    }
}
