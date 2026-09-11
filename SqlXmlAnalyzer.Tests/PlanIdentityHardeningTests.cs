using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Controls;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanIdentityHardeningTests
{
    private static XNamespace Ns => PlanIdentityModelTests.Ns;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexSummary_HasStatementOwnershipWithoutBorrowingAnOperator(bool nodeEntry)
    {
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
            "<StmtSimple StatementText='SELECT Id FROM sales.T;'><QueryPlan><RelOp NodeId='0'><TableScan>" +
            "<Object Database='dbA' Schema='sales' Table='T'/></TableScan></RelOp></QueryPlan></StmtSimple>" +
            "<StmtSimple StatementText='SELECT UserId FROM audit.Users WHERE UserId = 1;'><QueryPlan><RelOp NodeId='0'><TableScan>" +
            "<Object Database='dbA' Schema='audit' Table='Users'/></TableScan></RelOp></QueryPlan></StmtSimple>"));
        var engine = new RuleEngine();
        engine.RegisterRule(new SargableIndexRecommendationRule());
        var results = nodeEntry ? engine.AnalyzeNode(document.Descendants(Ns + "RelOp").Last(), Ns)
            : engine.AnalyzePlan(document, Ns);
        var summary = results.Should().ContainSingle().Subject;
        summary.Message.Should().Contain("[audit].[Users]");
        summary.Location!.DocumentId.Should().Be(PlanIdentityAdapter.GetDocument(document)!.Envelope.DocumentId);
        summary.Location.Statement.Should().Be(PlanIdentityAdapter.GetDocument(document)!.Statements[1].Key);
        summary.Location.Operator.Should().BeNull();
        summary.Objects.Should().BeEmpty();
        summary.NodeId.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("abc")]
    [InlineData("1.0")]
    public void InvalidNodeId_UsesOrdinalAndPreservesOriginalXml(string raw)
    {
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
            $"<StmtSimple><QueryPlan><RelOp NodeId='{raw}'/></QueryPlan></StmtSimple>"));
        var plan = PlanIdentityAdapter.GetDocument(document)!;
        var op = plan.Operators.Single();
        op.Key.NodeId.Should().BeNull();
        op.Key.OperatorOrdinal.Should().Be(1);
        plan.GetOperatorSource(op.Key)!.Attribute("NodeId")!.Value.Should().Be(raw);
        plan.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("PLAN_NODE_ID_INVALID");
    }

    [Fact]
    public void StatementSummary_PreservesTargetEvidenceWithoutDuplicateGlobalFinding()
    {
        var document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
            "<StmtSimple StatementText='SELECT Id FROM sales.T;'><QueryPlan><RelOp NodeId='0'/></QueryPlan></StmtSimple>" +
            "<StmtSimple StatementText='SELECT UserId FROM audit.Users WHERE UserId = 1;'><QueryPlan><RelOp NodeId='0'><NestedLoops>" +
            "<RelOp NodeId='1' PhysicalOp='Table Scan'><TableScan><Object Database='dbA' Schema='audit' Table='Users'/>" +
            "</TableScan></RelOp></NestedLoops></RelOp></QueryPlan></StmtSimple>"));
        var engine = new RuleEngine();
        engine.RegisterRule(new SargableIndexRecommendationRule());
        var results = engine.AnalyzePlan(document, Ns);
        var local = results.Should().ContainSingle().Subject;
        local.ResultScope.Should().Be(RuleScope.Statement);
        local.Location!.Statement.Should().Be(PlanIdentityAdapter.GetDocument(document)!.Statements[1].Key);
        local.Location.Operator.Should().BeNull();
        local.Diagnostic!.Evidence.Should().Contain(e => e.Name == "Target" && e.Value!.Contains("[audit].[Users]"));
    }

    [Fact]
    public void EquivalentNumericNodeIds_AreDiagnosedButKeepSeparateOperators()
    {
        var plan = PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
            "<StmtSimple><QueryPlan><RelOp NodeId='01'><NestedLoops><RelOp NodeId='+1'/></NestedLoops></RelOp></QueryPlan></StmtSimple>")))!;
        plan.Operators.Select(op => op.Key.NodeId).Should().Equal("1", "1");
        plan.Operators.Select(op => op.Key).Should().OnlyHaveUniqueItems();
        plan.Diagnostics.Single().Code.Should().Be("PLAN_NODE_ID_DUPLICATE");
    }

    [Theory]
    [InlineData("+0001", "1")]
    [InlineData(" 1 ", "1")]
    [InlineData("2147483647", "2147483647")]
    [InlineData("-2147483648", "-2147483648")]
    public void ValidNodeId_UsesBoundedCanonicalInteger(string raw, string expected)
    {
        var plan = PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
            $"<StmtSimple><QueryPlan><RelOp NodeId='{raw}'/></QueryPlan></StmtSimple>")))!;
        plan.Operators.Single().Key.NodeId.Should().Be(expected);
        plan.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData('1')]
    [InlineData('0')]
    public void LongNodeId_DoesNotExpandInEveryChildParentKey(char digit)
    {
        string raw = new(digit, 8192);
        string children = string.Concat(Enumerable.Range(1, 1000).Select(i => $"<RelOp NodeId='{i}'/>"));
        var input = new InputRecognitionService().Parse(PlanIdentityModelTests.Wrap(
            $"<StmtSimple><QueryPlan><RelOp NodeId='{raw}'><Concat>{children}</Concat></RelOp></QueryPlan></StmtSimple>"));
        input.IsSuccess.Should().BeTrue();
        var plan = PlanIdentityAdapter.GetDocument(input.Document)!;
        string json = JsonSerializer.Serialize(plan);
        json.Length.Should().BeLessThan(2_000_000, "raw NodeId must not multiply across parent references");
        json.Should().NotContain(raw);
        plan.Operators.Should().HaveCount(1001);
        plan.Operators.Select(op => op.Key).Should().OnlyHaveUniqueItems();
        plan.Operators.Skip(1).Should().OnlyContain(op => op.Parent == plan.Operators[0].Key);
        plan.GetOperatorSource(plan.Operators[0].Key)!.Attribute("NodeId")!.Value.Should().Be(raw);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComparisonUi_SelectionErrorsDoNotInterruptSwapClearOrRecovery(bool stale)
    {
        RunOnStaThread(() =>
        {
            var vm = new MainViewModel();
            var treeA = new TreeView();
            var treeB = new TreeView();
            var reporter = new CountingReporter();
            var comparison = new PlanComparisonUiActionService(new PlanComparisonController(unexpectedErrors: reporter),
                new PlanComparisonTreeService(), new PlanComparisonTreeViewRenderer(), vm, new TabControl(), treeA, treeB, Ns, reporter);
            vm.PropertyChanged += comparison.HandleViewModelPropertyChanged;
            var first = new PlanSnapshot { Document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap(
                "<StmtSimple><QueryPlan><RelOp NodeId='0' PhysicalOp='Table Scan'/></QueryPlan></StmtSimple>")) };
            var second = new PlanSnapshot { Document = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Fixture) };
            if (stale) second.SelectedQueryPlan = first.IdentityModel!.QueryPlans.Single().Key;
            vm.PlanA = first;
            vm.PlanB = second;
            if (stale) treeA.Items.OfType<TextBlock>().Should().ContainSingle().Which.Text.Should().Contain("PLAN_SELECTION_");
            else treeA.Items.OfType<TreeViewItem>().Should().NotBeEmpty();
            var tuning = new TuningSessionUiActionService(new TuningSessionActionService(new NoDialogs()), vm, () => null);
            tuning.SwapPlans();
            vm.PlanA.Should().BeSameAs(second);
            vm.PlanB.Should().BeSameAs(first);
            vm.ClearResults();
            vm.PlanA.Should().BeNull();
            vm.PlanB.Should().BeNull();
            treeA.Items.Count.Should().Be(0);
            treeB.Items.Count.Should().Be(0);
            second.SelectedQueryPlan = second.IdentityModel!.QueryPlans.First().Key;
            vm.PlanA = second;
            vm.PlanB = first;
            treeA.Items.OfType<TreeViewItem>().Should().HaveCount(2, "未匹配语句在两侧保留占位");
            treeB.Items.OfType<TreeViewItem>().Should().HaveCount(2);
            reporter.Calls.Should().Be(0, "missing/stale selections are expected failures");
        });
    }

    internal static void RunOnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class NoDialogs : IFileDialogService
    {
        public string? ShowOpenFile(FileDialogRequest request) => null;
        public string? ShowSaveFile(FileDialogRequest request) => null;
    }

    private sealed class CountingReporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation)
        { Calls++; return new(null, null, "unexpected test call"); }
    }
}
