using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanAnalysisSourceTests
{
    internal static InputRecognitionResult Input(string sql) => new InputRecognitionService().Parse(
        PlanIdentityModelTests.Wrap($"<StmtSimple StatementText='{sql}'><QueryPlan><RelOp NodeId='0' PhysicalOp='Constant Scan' LogicalOp='Constant Scan' EstimateRows='1'/></QueryPlan></StmtSimple>"));

    [Fact]
    public void Source_RejectsDocumentFromDifferentInput()
    {
        var a = Input("select 1"); var b = Input("select 2");
        Action create = () => new PlanAnalysisSource(a.Document!, "a.sqlplan", b);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Analyze_UsesCapturedPlanWhenOriginalPathChangesOrDisappears()
    {
        var input = Input("select 1"); var files = new Files(); var engine = new CaptureEngine();
        files.WriteAllText("source.sqlplan", Input("select 2").Document!.ToString());
        using var temporary = new TemporaryFileManager();
        var controller = Controller(files, engine, temporary);
        foreach (bool removed in new[] { false, true })
        {
            if (removed) files.Content.Remove("source.sqlplan");
            var result = await controller.AnalyzeAsync(input.Document!, "source.sqlplan", InputRecognitionService.ShowPlanNamespace, input: input);
            result.Input.Should().BeSameAs(input);
            engine.LastInput.Should().BeNull("IMP-26 reuses the completed diagnostic snapshot instead of analyzing twice");
            result.Analysis.Plan!.GetStatementSource(result.Analysis.Plan.Statements[0].Key)!.Document.Should().BeSameAs(input.Document);
            result.Analysis.RefactoredSql.Should().Be("select 1");
        }
        files.Reads.Should().BeEmpty("neither the plan nor a temporary SQL file should be read again");
    }

    [Fact]
    public async Task CancelledPendingAnalysis_DoesNotReturnAResultForCommit()
    {
        var b = Input("select 2");
        var files = new Files(); var engine = new CaptureEngine(); var refactor = new PassThrough { Block = true };
        using var temporary = new TemporaryFileManager();
        using var cancellation = new CancellationTokenSource();
        Task<PlanDocumentResult> pending = Controller(files, engine, temporary, refactor).AnalyzeAsync(
            b.Document!, "b.sqlplan", InputRecognitionService.ShowPlanNamespace, cancellation.Token, b);
        try
        {
            await refactor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
        }
        finally { refactor.Release.TrySetResult(true); }
        Func<Task> finish = async () => await pending;
        await finish.Should().ThrowAsync<OperationCanceledException>();
        refactor.LastReport!.InputEnvelope.Should().BeSameAs(b.Envelope);
    }

    [Fact]
    public void Apply_CommitsResultProvenanceAndHistoryDoesNotBorrowOldInput()
    {
        RunSta(() =>
        {
            var a = Input("select 1"); var b = Input("select 2");
            var vm = new MainViewModel { CurrentPlanInput = b, CurrentPlanFilePath = "b.sqlplan", CurrentPlanDoc = b.Document };
            var graphTabs = new TabControl();
            graphTabs.Items.Add(new TabItem()); graphTabs.Items.Add(new TabItem());
            graphTabs.SelectedIndex = 0;
            var service = new PlanAnalysisUiActionService(vm, new TabControl(), graphTabs);
            var engine = new Core.Rules.RuleEngine(configuration: RuleConfigurationDocument.Defaults); engine.RegisterDefaultRules();
            var output = new PlanAnalysisOutput("", "select 1", a.Document!.ToString(), "", [], "select 1")
                { Diagnostics = engine.AnalyzePlanDetailed(a.Document, InputRecognitionService.ShowPlanNamespace) };
            var result = new PlanDocumentResult(a.Document, "a.sqlplan", InputRecognitionService.ShowPlanNamespace, output) { Input = a };
            service.Apply(result);
            graphTabs.SelectedIndex.Should().Be(0, "opening a plan preserves the selected graph view");
            vm.CurrentPlanSource!.Document.Should().BeSameAs(a.Document);
            vm.CurrentPlanSource.Input.Should().BeSameAs(a);
            vm.CurrentPlanFilePath.Should().Be("a.sqlplan");
            vm.CurrentPlanInput.Should().BeSameAs(a);
            graphTabs.SelectedIndex = 1;
            service.Apply(result with { FilePath = "history.sqlplan", Input = null });
            graphTabs.SelectedIndex.Should().Be(1, "opening history preserves the hotspot view");
            vm.CurrentPlanSource!.FilePath.Should().Be("history.sqlplan");
            vm.CurrentPlanInput.Should().BeNull();
            vm.ClearResults(); vm.CurrentPlanSource.Should().BeNull(); vm.CurrentPlanFilePath.Should().BeNull();
        });
    }

    internal static PlanDocumentController Controller(Files files, CaptureEngine engine, TemporaryFileManager temporary, PassThrough? refactor = null) => new(new PlanAnalysisService(
        new ApplicationOrchestrator(engine, refactor ?? new PassThrough(), files, new Reporter(), NullLogger<ApplicationOrchestrator>.Instance),
        files, temporary, configuration: new(RuleConfigurationDocument.Defaults)));

    internal sealed class CaptureEngine : IAnalysisEngine
    {
        public InputRecognitionResult? LastInput;
        public AnalysisReport Analyze(string xmlContent) => new(Array.Empty<IAnalysisIssue>());
        public AnalysisReport AnalyzeInput(InputRecognitionResult input)
        {
            LastInput = input;
            return Analyze(input.Document!.ToString());
        }
    }

    internal sealed class Files : IFileHandler
    {
        public Dictionary<string, string> Content { get; } = new();
        public List<string> Reads { get; } = new();
        public Stream OpenRead(string path) => new MemoryStream(Encoding.UTF8.GetBytes(ReadAllText(path)));
        public string ReadAllText(string path) { Reads.Add(path); return Content[path]; }
        public void WriteAllText(string path, string contents) => Content[path] = contents;
        public bool Exists(string path) => Content.ContainsKey(path);
    }
    internal sealed class PassThrough : IRefactoringEngine
    {
        public bool Block;
        public AnalysisReport? LastReport;
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun)
        {
            LastReport = report; Entered.TrySetResult(true);
            if (Block) Release.Task.GetAwaiter().GetResult();
            return new(sql, true, [], new RefactorContext(sql));
        }
    }
    internal sealed class Reporter : IResultReporter
    {
        public void Report(RefactorResult result) { }
        public void Report(RefactorResult result, bool isDryRun, string? outputPath = null) { }
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        thread.Join(TimeSpan.FromSeconds(20)).Should().BeTrue();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
