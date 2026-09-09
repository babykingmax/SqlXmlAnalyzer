using System.Xml.Linq;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanCapabilityConsistencyTests
{
    private static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;
    private const DocumentCapabilities Base = DocumentCapabilities.PlanStatements | DocumentCapabilities.PreservedSource;

    public static TheoryData<string, DocumentCapabilities> Inputs => new()
    {
        { "", Base },
        { "<StmtSimple/>", Base },
        { "<StmtSimple><QueryPlan><RelOp NodeId='1'/></QueryPlan></StmtSimple>", Base | DocumentCapabilities.PlanOperators },
        { "<StmtSimple><QueryPlan><RelOp NodeId='1'><RunTimeInformation><RunTimeCountersPerThread Thread='0'/></RunTimeInformation></RelOp></QueryPlan></StmtSimple>", Base | DocumentCapabilities.PlanOperators | DocumentCapabilities.RuntimeCounters },
        { "<StmtSimple><QueryPlan><InternalInfo><RelOp><RunTimeInformation><RunTimeCountersPerThread/></RunTimeInformation></RelOp></InternalInfo></QueryPlan></StmtSimple>", Base },
        { "<StmtSimple><QueryPlan><RelOp NodeId='1'><InternalInfo><RunTimeCountersPerThread/></InternalInfo></RelOp></QueryPlan></StmtSimple>", Base | DocumentCapabilities.PlanOperators },
        { "<StmtSimple><QueryPlan><RelOp NodeId='1'><OutputList><RunTimeCountersPerThread/></OutputList></RelOp></QueryPlan></StmtSimple>", Base | DocumentCapabilities.PlanOperators },
        { "<StmtSimple><QueryPlan><InternalInfo><extension xmlns='urn:extension'><RelOp xmlns='http://schemas.microsoft.com/sqlserver/2004/07/showplan'/></extension></InternalInfo></QueryPlan></StmtSimple>", Base }
    };

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Analyze_ReaderDefaultAndExplicitEntryUseTheSameOwnedContent(string statements, DocumentCapabilities expected)
    {
        var input = new InputRecognitionService().Parse(PlanIdentityModelTests.Wrap(statements));
        input.IsSuccess.Should().BeTrue();
        var engine = new RuleEngine();
        var direct = engine.AnalyzePlanDetailed(input.Document!, Ns);
        var explicitInput = engine.AnalyzePlanDetailed(input.Document!, Ns, capabilities: input.Capabilities);
        input.Capabilities.Should().Be(expected);
        direct.Capabilities.Should().Be(expected);
        explicitInput.Capabilities.Should().Be(expected);
        foreach (var op in direct.Context.Model.Operators)
            engine.AnalyzeNodeDetailed(PlanIdentityAdapter.GetDocument(input.Document)!.GetOperatorSource(op.Key)!, Ns)
                .Capabilities.Should().Be(expected);
    }

    [Fact]
    public void Analyze_MutatedAndReparentedRuntimeRecomputesCapabilities()
    {
        var input = new InputRecognitionService().Parse(PlanIdentityModelTests.Wrap("<StmtSimple><QueryPlan><RelOp NodeId='1'/></QueryPlan></StmtSimple>"));
        var doc = input.Document!;
        var op = doc.Descendants(Ns + "RelOp").Single();
        var engine = new RuleEngine();
        var before = engine.AnalyzeNodeDetailed(op, Ns);
        var runtime = new XElement(Ns + "RunTimeInformation", new XElement(Ns + "RunTimeCountersPerThread"));
        op.Add(runtime);
        var after = engine.AnalyzeNodeDetailed(op, Ns);
        after.Capabilities.Should().Be(Base | DocumentCapabilities.PlanOperators | DocumentCapabilities.RuntimeCounters);
        before.Capabilities.Should().Be(Base | DocumentCapabilities.PlanOperators);
        runtime.Remove();
        op.Add(new XElement(Ns + "InternalInfo", runtime));
        engine.AnalyzeNodeDetailed(op, Ns).Capabilities.Should().Be(before.Capabilities);
        InputRecognitionService.Recognize(doc).Capabilities.Should().Be(before.Capabilities);
    }

    [Fact]
    public void Analyze_ExplicitRestrictionAndCancellationRemainAuthoritative()
    {
        var doc = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap("<StmtSimple/>"));
        var engine = new RuleEngine();
        engine.AnalyzePlanDetailed(doc, Ns, capabilities: DocumentCapabilities.None).Capabilities.Should().Be(DocumentCapabilities.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var analyze = () => engine.AnalyzePlanDetailed(doc, Ns, cancellationToken: cancellation.Token);
        analyze.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("<StmtSimple><QueryPlan><InternalInfo><RelOp><RunTimeInformation><RunTimeCountersPerThread/></RunTimeInformation></RelOp></InternalInfo></QueryPlan></StmtSimple>")]
    public void ReadCliScanAndAnalysis_ExportTheSameCapabilitiesAsDefaultDiagnostics(string statements)
    {
        string path = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-Capability-" + Guid.NewGuid().ToString("N") + ".sqlplan");
        try
        {
            File.WriteAllText(path, PlanIdentityModelTests.Wrap(statements));
            var input = new InputRecognitionService().Load(path);
            var analysis = new Analysis.SqlXmlAnalysisEngine(ruleEngineFactory: () => new RuleEngine()).AnalyzeInput(input);
            var scan = CLI.Program.ScanPlanFile(path, null, null, false, new(), default, () => new RuleEngine());
            var direct = new RuleEngine().AnalyzePlanDetailed(input.Document!, Ns);
            var (code, output) = DocumentReadContractTests.RunCli("read", path);
            code.Should().Be(0);
            using var json = JsonDocument.Parse(output);
            json.RootElement.GetProperty("Capabilities").GetString().Should().Be(Base.ToString());
            scan.Capabilities.Should().Be(Base.ToString());
            analysis.Capabilities.Should().Be(Base);
            scan.Diagnostics!.Capabilities.Should().Be(Base);
            analysis.Diagnostics!.Capabilities.Should().Be(Base);
            direct.Capabilities.Should().Be(Base);
        }
        finally { File.Delete(path); }
    }
}
