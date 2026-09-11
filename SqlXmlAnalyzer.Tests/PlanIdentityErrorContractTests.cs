using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanIdentityErrorContractTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp10Errors-{Guid.NewGuid():N}");
    public PlanIdentityErrorContractTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CliRead_CancelDuringOrAfterModelBuild_Returns130WithoutSuccessOutput(bool afterBuild)
    {
        string path = Path.Combine(_directory, "plan.sqlplan");
        File.WriteAllText(path, PlanIdentityModelTests.Fixture);
        using var cancellation = new CancellationTokenSource();
        var builder = new CancellingBuilder(cancellation, afterBuild);
        TextWriter original = Console.Out;
        using var output = new StringWriter();
        int code;
        try
        {
            Console.SetOut(output);
            code = Program.RunReadCommand(path, new DocumentReadOptions(), cancellation.Token, builder);
        }
        finally { Console.SetOut(original); }
        builder.Calls.Should().Be(1);
        builder.ObservedToken.Should().Be(cancellation.Token);
        code.Should().Be(130);
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void CliRead_LongParentNodeId_ReturnsBoundedJsonWithExplicitDiagnostic()
    {
        string path = Path.Combine(_directory, "long-id.sqlplan");
        string raw = new('1', 8192);
        string children = string.Concat(Enumerable.Range(1, 1000).Select(i => $"<RelOp NodeId='{i}'/>"));
        string xml = PlanIdentityModelTests.Wrap(
            $"<StmtSimple><QueryPlan><RelOp NodeId='{raw}'><Concat>{children}</Concat></RelOp></QueryPlan></StmtSimple>");
        File.WriteAllText(path, xml);
        var result = DocumentReadContractTests.RunCli("read", path);
        result.Code.Should().Be(0);
        string baselinePath = Path.Combine(_directory, "bounded-id.sqlplan");
        File.WriteAllText(baselinePath, xml.Replace(raw, "0", StringComparison.Ordinal));
        var baseline = DocumentReadContractTests.RunCli("read", baselinePath);
        baseline.Code.Should().Be(0);
        result.Output.Length.Should().BeLessThan(baseline.Output.Length + 10_000,
            "an invalid identifier may add a diagnostic, but must not expand each child parent key");
        result.Output.Should().NotContain(raw);
        using var json = JsonDocument.Parse(result.Output);
        var plan = json.RootElement.GetProperty("Plan");
        plan.GetProperty("Diagnostics")[0].GetProperty("Code").GetString().Should().Be("PLAN_NODE_ID_INVALID");
        var operators = plan.GetProperty("Batches")[0].GetProperty("Statements")[0]
            .GetProperty("QueryPlans")[0].GetProperty("Operators");
        operators.GetArrayLength().Should().Be(1001);
        operators[0].GetProperty("Key").GetProperty("NodeId").ValueKind.Should().Be(JsonValueKind.Null);
        operators[1].GetProperty("Parent").GetProperty("OperatorOrdinal").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ComparisonUi_LoadSessionRestoresBothSelectionsAndClearHistoryCompletes()
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var sessions = new TuningSessionService();
            var first = sessions.CaptureSnapshot(SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Fixture), "multi.sqlplan", 1);
            var second = sessions.CaptureSnapshot(SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
                "<StmtSimple><QueryPlan><RelOp NodeId='0'/></QueryPlan></StmtSimple>")), "single.sqlplan", 2);
            string path = Path.Combine(_directory, "session.xml");
            sessions.Save(path, [first, second], first, second);
            var vm = new MainViewModel();
            var treeA = new TreeView();
            var treeB = new TreeView();
            var ui = new PlanComparisonUiActionService(new PlanComparisonController(), new PlanComparisonTreeService(),
                new PlanComparisonTreeViewRenderer(), vm, new TabControl(), treeA, treeB, PlanIdentityModelTests.Ns);
            vm.PropertyChanged += ui.HandleViewModelPropertyChanged;
            vm.LoadSession(path);
            vm.PlanA!.FilePath.Should().Be(first.FilePath);
            vm.PlanB!.FilePath.Should().Be(second.FilePath);
            vm.PlanA.Should().BeSameAs(vm.TuningHistory[0]);
            vm.PlanB.Should().BeSameAs(vm.TuningHistory[1]);
            vm.TuningHistory.Should().HaveCount(2);
            vm.StatusText.Should().Contain("已成功载入");
            vm.ClearHistoryCommand.Execute(null);
            vm.PlanA.Should().BeNull();
            vm.PlanB.Should().BeNull();
            vm.TuningHistory.Should().BeEmpty();
            treeA.Items.Count.Should().Be(0);
            treeB.Items.Count.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComparisonUi_UnexpectedFailureKeepsSelectionAndReportsDumpOutcome(bool dumpFails)
    {
        Logger.Shutdown();
        string logPath = Path.Combine(_directory, "comparison.log");
        Logger.Initialize(customLogFilePath: logPath);
        var writer = new CountingDumpWriter(dumpFails);
        var reporter = new UnexpectedErrorReporter(writer, Path.Combine(_directory, "dumps"));
        var failure = new InvalidOperationException("IMP10 synthetic comparison fault");
        try
        {
            PlanIdentityHardeningTests.RunOnStaThread(() =>
            {
                var vm = new MainViewModel();
                var treeA = new TreeView();
                var treeB = new TreeView();
                var controller = new PlanComparisonController(new FailingMetrics(failure), reporter);
                var ui = new PlanComparisonUiActionService(controller, new PlanComparisonTreeService(),
                    new PlanComparisonTreeViewRenderer(), vm, new TabControl(), treeA, treeB,
                    PlanIdentityModelTests.Ns, reporter);
                vm.PropertyChanged += ui.HandleViewModelPropertyChanged;
                var snapshot = new PlanSnapshot { Document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
                    "<StmtSimple><QueryPlan><RelOp NodeId='0'/></QueryPlan></StmtSimple>")) };
                vm.PlanA = snapshot;
                vm.PlanB = snapshot;
                vm.PlanA.Should().BeSameAs(snapshot);
                vm.PlanB.Should().BeSameAs(snapshot);
                treeA.Items.OfType<TextBlock>().Single().Text.Should().Contain(dumpFails ? "DUMP 生成失败" : "DUMP：");
                vm.ClearResults();
                vm.PlanA.Should().BeNull();
                vm.PlanB.Should().BeNull();
                treeA.Items.Count.Should().Be(0);
            });
            writer.Calls.Should().Be(1, "the same failure crossing controller/UI boundaries must be captured once");
            var incident = reporter.Report(failure, "already-captured");
            incident.DumpCreated.Should().Be(!dumpFails);
            if (!dumpFails) MinidumpValidator.Validate(incident.DumpPath!);
            File.ReadAllText(incident.MetadataPath!).Should().Contain("PlanComparisonController.BuildComparison");
            Logger.Debug("IMP10 comparison debug probe");
            Logger.Warning("IMP10 comparison warning probe");
            Logger.Error("IMP10 comparison error probe");
        }
        finally { Logger.Shutdown(); }
        string log = File.ReadAllText(logPath);
        log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
    }

    private sealed class CancellingBuilder(CancellationTokenSource cancellation, bool afterBuild) : IPlanDocumentBuilder
    {
        public int Calls { get; private set; }
        public CancellationToken ObservedToken { get; private set; }
        public PlanDocument Build(XDocument document, DocumentEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Calls++;
            ObservedToken = cancellationToken;
            if (!afterBuild) cancellation.Cancel();
            var plan = new PlanDocumentBuilder().Build(document, envelope, cancellationToken);
            if (afterBuild) cancellation.Cancel();
            return plan;
        }
    }

    private sealed class FailingMetrics(Exception failure) : IPlanComparisonRuntimeMetricsReader
    {
        public PlanComparisonRuntimeMetrics Read(XElement element) => throw failure;
    }

    private sealed class CountingDumpWriter(bool fail) : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path)
        {
            Calls++;
            if (fail) throw new IOException("synthetic dump storage failure");
            new WindowsMiniDumpWriter().Write(path);
        }
    }

    public void Dispose()
    {
        string path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp10Errors-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup target");
        Directory.Delete(path, recursive: true);
    }
}
