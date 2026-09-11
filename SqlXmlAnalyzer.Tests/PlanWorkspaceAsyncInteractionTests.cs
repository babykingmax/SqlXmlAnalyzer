using System.Windows.Controls;
using System.Windows;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;
using SqlXmlAnalyzer.Views;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanWorkspaceAsyncInteractionTests
{
    [Fact]
    public void CompletedPlan_WhenDeadlockLoadBegins_RemainsNavigable() => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var (model, view, _, service, sessions) = CreateWorkspace(open: false);
        using (sessions)
        {
            string operators = string.Concat(Enumerable.Range(1, 129)
                .Select(id => $"<RelOp NodeId='{id}' PhysicalOp='Constant Scan' EstimateRows='1'/>"));
            var input = new InputRecognitionService().Parse(PlanIdentityModelTests.Wrap(
                $"<StmtSimple StatementText='SELECT 1'><QueryPlan><RelOp NodeId='0' PhysicalOp='Concatenation' EstimateRows='1'><Concat>{operators}</Concat></RelOp></QueryPlan></StmtSimple>"));
            model.PlanWorkspace.Open(input.Document!, WorkspaceSelectionTests.Report(input.Document!), [], input);
            (await service.PendingRender).Should().BeTrue();
            var retainedNodes = view.NodifyGraph.AllNodes.ToArray();

            sessions.Begin(AnalysisDocumentKind.DeadlockXml);
            await view.NodifyGraph.NavigatePageAsync(64);

            view.NodifyGraph.Nodes.Should().Equal(retainedNodes.Skip(64).Take(64));
            view.NodifyGraph.PendingPage.IsCompletedSuccessfully.Should().BeTrue();
            model.PlanWorkspace.Document.Should().BeSameAs(input.Document);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearResults_RemovesSqlAndStatisticsDuringCompletedOrPendingRender(bool pending) => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var (model, view, diff, service, sessions) = CreateWorkspace();
        using (sessions)
        {
            (await service.PendingRender).Should().BeTrue();
            diff.SetSql("SELECT 'previous original'", "SELECT 'previous candidate'", null);
            model.CurrentRewriteSource = "previous source";
            XNamespace ns = InputRecognitionService.ShowPlanNamespace;
            var statistics = new XDocument(new XElement(ns + "QueryPlan",
                new XElement(ns + "ParameterList", new XElement(ns + "ColumnReference",
                    new XAttribute("Column", "@previous"), new XAttribute("ParameterCompiledValue", "1"), new XAttribute("ParameterRuntimeValue", "20"))),
                new XElement(ns + "OptimizerStatsUsage", new XElement(ns + "StatisticsInfo",
                    new XAttribute("Database", "[previous_db]"), new XAttribute("Schema", "[dbo]"),
                    new XAttribute("Table", "[previous_table]"), new XAttribute("Statistics", "[previous_stats]")))));
            new PlanStatisticsUiActionService(view.StatisticsHistogram).LoadFromPlan(statistics, ns);
            var statsInput = (TextBox)view.StatisticsHistogram.FindName("TxtStatsInput");
            statsInput.Text.Should().Contain("previous_table");
            Task render = Task.CompletedTask;
            if (pending)
            {
                model.PlanWorkspace.SelectedStatement = model.PlanWorkspace.Statements[1];
                render = service.PendingRender;
            }

            model.ClearResults();
            await render;
            await service.PendingRender;

            diff.CurrentOriginalSql.Should().BeEmpty();
            diff.CurrentRefactoredSql.Should().BeEmpty();
            model.CurrentRewriteSource.Should().BeEmpty();
            model.CurrentRewriteReview.Should().BeNull();
            model.PlanStatementText.Should().BeEmpty();
            model.PlanWarningsText.Should().BeEmpty();
            view.NodifyGraph.AllNodes.Should().BeEmpty();
            ((TextBlock)view.StatisticsHistogram.FindName("TxtParamName")).Text.Should().NotContain("previous");
            ((DataGrid)view.StatisticsHistogram.FindName("GridStatsUsage")).Items.Count.Should().Be(0);
            ((Canvas)view.StatisticsHistogram.FindName("DrawCanvas")).Children.Count.Should().Be(0);
            statsInput.Text.Should().NotContain("previous");
        }
    });

    [Fact]
    public void DocumentCommit_WhenPresentationFails_DoesNotTreatClearedSelectionAsSuccess() => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var (model, view, _, service, sessions) = CreateWorkspace(open: false);
        using (sessions)
        {
            var request = sessions.Begin();
            sessions.Report(request.RequestId, AnalysisOperationState.PreparingView);
            using var scope = service.BeginDocumentRender(request.RequestId);
            view.NodifyGraph.Nodes.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                    throw new System.IO.InvalidDataException("Synthetic invalid presentation input.");
            };
            var input = WorkspaceSelectionTests.Input();
            model.PlanWorkspace.Open(input.Document!, WorkspaceSelectionTests.Report(input.Document!), [], input);
            (await service.WaitForPendingRenderAsync()).Should().BeFalse();
            sessions.Progress.State.Should().Be(AnalysisOperationState.Failed);
            view.NodifyGraph.AllNodes.Should().BeEmpty();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DocumentCommit_WhenSelectionIsReplaced_WaitsForLatestWithoutStealingCompletion(bool cancel) => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var (model, view, _, service, sessions) = CreateWorkspace(open: false);
        using (sessions)
        {
            var request = sessions.Begin();
            sessions.Report(request.RequestId, AnalysisOperationState.PreparingView);
            using var scope = service.BeginDocumentRender(request.RequestId);
            var input = WorkspaceSelectionTests.Input();
            model.PlanWorkspace.Open(input.Document!, WorkspaceSelectionTests.Report(input.Document!), [], input);
            var waiting = service.WaitForPendingRenderAsync();
            model.PlanWorkspace.SelectedStatement = model.PlanWorkspace.Statements[1];
            if (cancel) sessions.CancelCurrent();
            (await waiting).Should().Be(!cancel);
            sessions.Progress.RequestId.Should().Be(request.RequestId);
            if (cancel)
            {
                sessions.Progress.State.Should().Be(AnalysisOperationState.Cancelled);
                view.NodifyGraph.AllNodes.Should().BeEmpty();
            }
            else
            {
                view.NodifyGraph.AllNodes.Select(n => n.RawElement).Should().Equal(model.PlanWorkspace.Selection!.Operators);
                sessions.Progress.State.Should().Be(AnalysisOperationState.PreparingView, "the document caller still has to commit SQL comparison and statistics");
                sessions.Report(request.RequestId, AnalysisOperationState.Ready).Should().BeTrue();
            }
        }
    });

    [Fact]
    public void DocumentScope_WhenOlderRequestFinishes_DoesNotReleaseTheNewOwner() => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var (model, _, _, service, sessions) = CreateWorkspace(open: false);
        using (sessions)
        {
            var old = service.BeginDocumentRender(sessions.Begin().RequestId);
            var latest = sessions.Begin();
            using var current = service.BeginDocumentRender(latest.RequestId);
            old.Dispose();
            sessions.Report(latest.RequestId, AnalysisOperationState.PreparingView);
            var input = WorkspaceSelectionTests.Input();
            model.PlanWorkspace.Open(input.Document!, WorkspaceSelectionTests.Report(input.Document!), [], input);
            (await service.WaitForPendingRenderAsync()).Should().BeTrue();
            sessions.Progress.RequestId.Should().Be(latest.RequestId);
            sessions.Progress.State.Should().Be(AnalysisOperationState.PreparingView);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selection_WhenReplacedDuringPreparation_CompletesLatestSelection(bool partial) => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var (model, view, diff, service, sessions) = CreateWorkspace();
        using (sessions)
        {
            await service.PendingRender;
            if (partial)
            {
                var operation = sessions.Begin();
                sessions.Report(operation.RequestId, AnalysisOperationState.Partial);
            }
            model.PlanWorkspace.SelectedStatement = model.PlanWorkspace.Statements[1];
            var old = service.PendingRender;
            model.PlanWorkspace.SelectedStatement = model.PlanWorkspace.Statements[0];
            await Task.WhenAll(old, service.PendingRender);
            sessions.Progress.State.Should().Be(partial ? AnalysisOperationState.Partial : AnalysisOperationState.Ready);
            diff.CurrentOriginalSql.Should().Be(model.PlanWorkspace.SelectedStatement!.Statement.Text);
            view.NodifyGraph.AllNodes.Select(n => n.RawElement).Should().Equal(model.PlanWorkspace.Selection!.Operators);
        }
    });

    [Fact]
    public void Selection_WhenCancelledAfterReplacement_RemovesIncompleteView() => PlanGraphAsyncInteractionTests.RunSta(async () =>
    {
        var (model, view, _, service, sessions) = CreateWorkspace();
        using (sessions)
        {
            await service.PendingRender;
            model.PlanWorkspace.SelectedStatement = model.PlanWorkspace.Statements[1];
            model.PlanWorkspace.SelectedStatement = model.PlanWorkspace.Statements[0];
            sessions.CancelCurrent();
            (await service.PendingRender).Should().BeFalse();
            sessions.Progress.State.Should().Be(AnalysisOperationState.Cancelled);
            view.NodifyGraph.AllNodes.Should().BeEmpty();
        }
    });

    private static (MainViewModel Model, PlanWorkspaceView View, SqlDiffUiActionService Diff,
        PlanWorkspaceUiActionService Service, AnalysisSessionCoordinator Sessions) CreateWorkspace(bool open = true)
    {
        // These tests exercise control state without materializing a window.
        // Full theme/template validation runs in the separate WPF probe process.
        if (System.Windows.Application.Current == null)
        {
            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var key in new[] { "MaterialDesignFlatButton", "MaterialDesignRaisedButton", "MaterialDesignOutlinedButton", "SecondaryButtonStyle" })
            {
                var style = new Style(typeof(Button));
                style.Seal();
                application.Resources[key] = style;
            }
        }
        var model = new MainViewModel();
        var view = new PlanWorkspaceView { DataContext = model };
        var diff = new SqlDiffUiActionService(new(), new(new()), new RichTextBox(), new RichTextBox(), view.StatementTextBox);
        var sessions = new AnalysisSessionCoordinator();
        var service = new PlanWorkspaceUiActionService(model, view, diff, new(view.StatisticsHistogram), sessions: sessions);
        if (open)
        {
            var input = WorkspaceSelectionTests.Input();
            model.PlanWorkspace.Open(input.Document!, WorkspaceSelectionTests.Report(input.Document!), [], input);
        }
        return (model, view, diff, service, sessions);
    }
}
