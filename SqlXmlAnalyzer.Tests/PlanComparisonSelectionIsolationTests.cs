using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;
using System.Windows.Controls;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanComparisonSelectionIsolationTests
{
    [Fact]
    public void SameSnapshotOnBothSides_CanCompareTwoDifferentStatements()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var vm = CreateWorkspace();
            var snapshot = PlanComparisonMultiStatementTests.Snapshot(
                PlanComparisonMultiStatementTests.Statement("SELECT 1", 1) + PlanComparisonMultiStatementTests.Statement("SELECT 2", 2));
            vm.PlanA = snapshot; vm.PlanB = snapshot;
            vm.ManualScopeA = vm.ComparisonScopesA[0]; vm.ManualScopeB = vm.ComparisonScopesB[1];
            vm.ApplyComparisonSelectionCommand.Execute(null);
            var pair = vm.Comparison!.Statements.Single();
            pair.A!.Statement.Key.StatementOrdinal.Should().Be(1);
            pair.B!.Statement.Key.StatementOrdinal.Should().Be(2);
            pair.RootsB.Single().CostDelta.Should().Be(1);
        });
    }

    internal static MainViewModel CreateWorkspace()
    {
        var vm = new MainViewModel();
        var ui = new PlanComparisonUiActionService(new PlanComparisonController(), new PlanComparisonTreeService(),
            new PlanComparisonTreeViewRenderer(), vm, new TabControl(), new TreeView(), new TreeView(), PlanComparisonMultiStatementTests.Ns);
        vm.PropertyChanged += ui.HandleViewModelPropertyChanged;
        return vm;
    }

    [Fact]
    public void SameSnapshotManualSelection_SurvivesSessionRoundtrip()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "imp17-side-selection-" + Guid.NewGuid().ToString("N") + ".pesession");
            try
            {
                var vm = CreateWorkspace();
                var snapshot = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 1)
                    + PlanComparisonMultiStatementTests.Statement("SELECT 2", 2));
                vm.TuningHistory.Add(snapshot); vm.PlanA = snapshot; vm.PlanB = snapshot;
                vm.ManualScopeA = vm.ComparisonScopesA[0]; vm.ManualScopeB = vm.ComparisonScopesB[1];
                vm.ApplyComparisonSelectionCommand.Execute(null); vm.SaveSession(path);
                var restored = CreateWorkspace(); restored.LoadSession(path);
                restored.PlanA.Should().BeSameAs(restored.PlanB);
                var pair = restored.Comparison!.Statements.Should().ContainSingle().Subject;
                pair.A!.Statement.Key.StatementOrdinal.Should().Be(1);
                pair.B!.Statement.Key.StatementOrdinal.Should().Be(2);
                restored.ManualScopeA!.Key.Should().Be(restored.ComparisonSelection.A);
                restored.ManualScopeB!.Key.Should().Be(restored.ComparisonSelection.B);
            }
            finally { File.Delete(path); }
        });
    }

    [Fact]
    public void SwappingSameSnapshot_SwapsSelectionsAndResetRestoresAllStatements()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var vm = CreateWorkspace();
            var snapshot = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 1)
                + PlanComparisonMultiStatementTests.Statement("SELECT 2", 2));
            vm.PlanA = snapshot; vm.PlanB = snapshot;
            vm.ManualScopeA = vm.ComparisonScopesA[0]; vm.ManualScopeB = vm.ComparisonScopesB[1];
            vm.ApplyComparisonSelectionCommand.Execute(null);
            new TuningSessionUiActionService(new TuningSessionActionService(), vm, () => null).SwapPlans();
            var pair = vm.Comparison!.Statements.Single();
            pair.A!.Statement.Key.StatementOrdinal.Should().Be(2);
            pair.B!.Statement.Key.StatementOrdinal.Should().Be(1);
            pair.RootsB.Single().CostDelta.Should().Be(-1);
            snapshot.SelectedQueryPlan.Should().BeNull("新的界面选择不能回写共享快照");
            vm.ResetComparisonSelectionCommand.Execute(null);
            vm.Comparison!.Statements.Should().HaveCount(2);
        });
    }

    [Fact]
    public void ReplacingOneSide_ResetsOnlyThatSidesSelection()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var vm = CreateWorkspace();
            var snapshot = PlanComparisonMultiStatementTests.Snapshot(PlanComparisonMultiStatementTests.Statement("SELECT 1", 1)
                + PlanComparisonMultiStatementTests.Statement("SELECT 2", 2));
            vm.PlanA = snapshot; vm.PlanB = snapshot;
            vm.ManualScopeA = vm.ComparisonScopesA[0]; vm.ManualScopeB = vm.ComparisonScopesB[1];
            vm.ApplyComparisonSelectionCommand.Execute(null);
            var selectionB = vm.ComparisonSelection.B;
            vm.PlanA = PlanComparisonMultiStatementTests.RuntimeSnapshot(10);
            vm.ComparisonSelection.A.Should().BeNull(); vm.ComparisonSelection.B.Should().Be(selectionB);
            vm.ManualScopeA.Should().BeNull();
        });
    }

    [Fact]
    public void StaleSideSelection_IsRejectedBeforeOverwritingSession()
    {
        var snapshot = PlanComparisonMultiStatementTests.RuntimeSnapshot(1);
        var selected = snapshot.IdentityModel!.QueryPlans.Single().Key;
        snapshot.Document.Root!.SetAttributeValue("Build", "17.0.1.1");
        string path = Path.Combine(Path.GetTempPath(), "imp17-stale-" + Guid.NewGuid().ToString("N") + ".pesession");
        try
        {
            File.WriteAllText(path, "existing-session");
            Action save = () => new TuningSessionService().Save(path, [snapshot], snapshot, snapshot, new(selected, selected));
            save.Should().Throw<InvalidDataException>().WithMessage("PLAN_SELECTION_NOT_FOUND*");
            File.ReadAllText(path).Should().Be("existing-session");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DuplicateSideSelection_IsRejected()
    {
        var snapshot = PlanComparisonMultiStatementTests.RuntimeSnapshot(1);
        string path = Path.Combine(Path.GetTempPath(), "imp17-duplicate-side-" + Guid.NewGuid().ToString("N") + ".pesession");
        try
        {
            var service = new TuningSessionService(); var key = snapshot.IdentityModel!.QueryPlans.Single().Key;
            service.Save(path, [snapshot], snapshot, snapshot, new(key, key));
            var saved = SafeXmlHelper.LoadSafe(path); XNamespace ns = "http://schemas.sqlxmlanalyzer.com/session";
            var selections = saved.Root!.Element(ns + "ComparisonSelection")!;
            selections.Add(new XElement(selections.Element(ns + "A")!));
            saved.Save(path, SaveOptions.DisableFormatting);
            Action load = () => service.Load(path);
            load.Should().Throw<InvalidDataException>().WithMessage("PLAN_SELECTION_NOT_FOUND*");
        }
        finally { File.Delete(path); }
    }
}
