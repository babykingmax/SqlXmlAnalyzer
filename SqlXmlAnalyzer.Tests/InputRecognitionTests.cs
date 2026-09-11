using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using FluentAssertions;
using SqlXmlAnalyzer.Analysis;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class InputRecognitionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp06Tests-{Guid.NewGuid():N}");
    private const string Deadlock = "<deadlock><process-list><process id='p1'/></process-list><resource-list><keylock/></resource-list></deadlock>";
    private const string Ns = InputRecognitionService.ShowPlanNamespace;

    public InputRecognitionTests() => Directory.CreateDirectory(_directory);

    public static TheoryData<string, InputStatus, string> InvalidInputs => new()
    {
        { "<notAPlan/>", InputStatus.Unrecognized, "INPUT_UNRELATED_XML" },
        { "<root><RelOp /></root>", InputStatus.Unrecognized, "INPUT_UNRELATED_XML" },
        { "<ShowPlanXML xmlns='urn:test:showplan'/>", InputStatus.Unsupported, "INPUT_PLAN_NAMESPACE" },
        { "<ShowPlanXML/>", InputStatus.Unsupported, "INPUT_PLAN_NAMESPACE" },
        { $"<ShowPlanXML xmlns='{Ns}'><BatchSequence/></ShowPlanXML>", InputStatus.Invalid, "INPUT_PLAN_STRUCTURE" },
        { $"<ShowPlanXML xmlns='{Ns}'><BatchSequence><Batch><Statements xmlns=''><StmtSimple/></Statements></Batch></BatchSequence></ShowPlanXML>", InputStatus.Invalid, "INPUT_PLAN_STRUCTURE" },
        { $"<ShowPlanXML xmlns='{Ns}'><BatchSequence><Batch><Statements><FutureStatement/></Statements></Batch></BatchSequence></ShowPlanXML>", InputStatus.Invalid, "INPUT_PLAN_STRUCTURE" },
        { "<deadlock><victim-list/></deadlock>", InputStatus.Invalid, "INPUT_DEADLOCK_STRUCTURE" },
        { "<deadlock-list/>", InputStatus.Invalid, "INPUT_DEADLOCK_STRUCTURE" },
        { "<event name='xml_deadlock_report'><data name='other'><value>" + Deadlock + "</value></data></event>", InputStatus.Invalid, "INPUT_DEADLOCK_STRUCTURE" },
        { "<root>" + Deadlock + "</root>", InputStatus.Unrecognized, "INPUT_UNRELATED_XML" },
        { "<ShowPlanXML", InputStatus.MalformedXml, "INPUT_MALFORMED_XML" },
        { "", InputStatus.MalformedXml, "INPUT_MALFORMED_XML" },
        { "<!DOCTYPE a [<!ENTITY secret SYSTEM 'file:///must-not-read'>]><a>&secret;</a>", InputStatus.MalformedXml, "INPUT_MALFORMED_XML" }
    };

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task InvalidInput_GuiAnalysisAndScanReturnSameFailure(string xml, InputStatus status, string code)
    {
        string path = Write("invalid.sqlplan", xml);
        DocumentOpenResult gui = await new DocumentOpenService().OpenAsync(path);
        AnalysisReport analysis = new SqlXmlAnalysisEngine().Analyze(xml);
        var cli = RunCli("--path", path, "--format", "json");
        gui.IsSuccess.Should().BeFalse();
        gui.Status.Should().Be(status);
        gui.ErrorCode.Should().Be(code);
        analysis.IsSuccess.Should().BeFalse();
        analysis.InputStatus.Should().Be(status);
        analysis.InputErrorCode.Should().Be(code);
        analysis.Issues.Should().ContainSingle(issue => issue.IssueType == code);
        cli.Code.Should().Be(1);
        using var json = JsonDocument.Parse(cli.Output);
        var scan = json.RootElement[0];
        scan.GetProperty("Status").GetString().Should().Be("Failed");
        scan.GetProperty("InputStatus").GetString().Should().Be(status.ToString());
        scan.GetProperty("InputErrorCode").GetString().Should().Be(code);
        scan.GetProperty("FailureMessage").GetString().Should().Contain(gui.ErrorMessage);
    }

    [Theory]
    [InlineData("plan_no_relop.sqlplan")]
    [InlineData("plan_prefixed_no_relop.sqlplan")]
    public async Task ValidNoRelOpFixture_PassesOfficialSchemaAndAllPlanEntries(string name)
    {
        string xml = Fixture(name);
        XDocument document = SafeXmlHelper.ParseSafe(xml);
        document.Descendants().Should().NotContain(e => e.Name.LocalName == "RelOp");
        AssertMatchesShowPlanSchema(document);
        string path = Write("renamed.xml", xml);
        DocumentOpenResult gui = await new DocumentOpenService().OpenAsync(path);
        gui.IsSuccess.Should().BeTrue();
        gui.Kind.Should().Be(AnalysisDocumentKind.ExecutionPlanXml);
        var analysis = new SqlXmlAnalysisEngine().Analyze(xml);
        analysis.IsSuccess.Should().BeTrue();
        analysis.InputErrorCode.Should().BeNull();
        RunCli("--path", path, "--format", "json").Code.Should().Be(0);
    }

    [Fact]
    public void SchemaValidEmptyStatementBlock_IsRecognizedWithoutInventingOperators()
    {
        XDocument document = SafeXmlHelper.ParseSafe(Fixture("plan_no_relop.sqlplan"));
        document.Descendants(XName.Get("Statements", Ns)).Single().RemoveNodes();
        AssertMatchesShowPlanSchema(document);
        InputRecognitionService.Recognize(document).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void PrefixedPlan_StillAppliesOperatorChecks()
    {
        string xml = $"<sp:ShowPlanXML xmlns:sp='{Ns}'><sp:BatchSequence><sp:Batch><sp:Statements><sp:StmtSimple><sp:QueryPlan><sp:RelOp NodeId='1' PhysicalOp='Table Scan' EstimatedTotalSubtreeCost='12'/></sp:QueryPlan></sp:StmtSimple></sp:Statements></sp:Batch></sp:BatchSequence></sp:ShowPlanXML>";
        var result = RunCli("--path", Write("prefixed.sqlplan", xml), "--block-scans", "--format", "json");
        result.Code.Should().Be(1);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement[0].GetProperty("ContainsScans").GetBoolean().Should().BeTrue();
        json.RootElement[0].GetProperty("MaxSubtreeCost").GetDouble().Should().Be(12);
        json.RootElement[0].GetProperty("InputStatus").GetString().Should().Be("Success");
    }

    [Theory]
    [InlineData("<deadlock-list>{0}</deadlock-list>")]
    [InlineData("<event name='xml_deadlock_report'><data name='xml_report'><value>{0}</value></data></event>")]
    [InlineData("<RingBufferTarget><event name='xml_deadlock_report'><data name='xml_report'><value>{0}</value></data></event></RingBufferTarget>")]
    [InlineData("{0}")]
    public async Task DeadlockWrappers_AreRecognizedRegardlessOfFileExtension(string wrapper)
    {
        string xml = string.Format(wrapper, Deadlock);
        var result = await new DocumentOpenService().OpenAsync(Write("misnamed.sqlplan", xml));
        result.IsSuccess.Should().BeTrue();
        result.Kind.Should().Be(AnalysisDocumentKind.DeadlockXml);
        result.Deadlocks.Should().ContainSingle();
        DeadlockXmlParser.TryParseDeadlockXml(result.Document).IsSuccess.Should().BeTrue();
        new DeadlockTimelineParser().ParseResult(xml).IsSuccess.Should().BeTrue();
        var analysis = new SqlXmlAnalysisEngine().Analyze(xml);
        analysis.IsSuccess.Should().BeFalse();
        analysis.InputErrorCode.Should().Be("INPUT_EXPECTED_PLAN");
        var cli = RunCli("--path", Write("wrong-kind.xml", xml), "--format", "json");
        cli.Code.Should().Be(1);
        cli.Output.Should().Contain("INPUT_EXPECTED_PLAN");
    }

    [Fact]
    public void MultipleNamespacedEvents_PreserveOrderTimestampAndSource_AndRequireSelection()
    {
        XDocument source = SafeXmlHelper.ParseSafe(Fixture("deadlock_multiple_events.xdl"));
        string before = source.ToString();
        var result = InputRecognitionService.Recognize(source);
        result.IsSuccess.Should().BeTrue();
        result.Deadlocks.Select(e => e.Index).Should().Equal(1, 2);
        result.Deadlocks[1].Timestamp.Should().Be("2026-09-08T08:01:00+08:00");
        result.Deadlocks[1].Document.Root!.Name.Namespace.Should().Be(XNamespace.None);
        var second = new DeadlockAnalysisService().Analyze(result.Deadlocks[1].Document);
        second.Processes.Select(p => p.Id).Should().Equal("p3", "p4");
        second.Timeline.Processes.Keys.Should().BeEquivalentTo("p3", "p4");
        DeadlockXmlParser.TryParseDeadlockXml(source).Errors.Should().Contain(e => e.Contains("INPUT_EVENT_SELECTION_REQUIRED"));
        new DeadlockTimelineParser().ParseResult(source.ToString()).Errors.Should().Contain(e => e.Contains("INPUT_EVENT_SELECTION_REQUIRED"));
        Action analyze = () => new DeadlockAnalysisService().Analyze(source);
        analyze.Should().Throw<InvalidDataException>().WithMessage("*INPUT_EVENT_SELECTION_REQUIRED*");
        source.ToString().Should().Be(before);
    }

    [Theory]
    [InlineData("<deadlock-list>" + Deadlock + "<deadlock/></deadlock-list>")]
    [InlineData("<deadlock-list>" + Deadlock + "<unknown/></deadlock-list>")]
    [InlineData("<events><event name='xml_deadlock_report'><data name='xml_report'><value>" + Deadlock + "</value></data></event><event name='other'/></events>")]
    public void MixedOrDamagedCollections_ReturnPartialWithExplicitSkippedRange(string xml)
    {
        var result = new InputRecognitionService().Parse(xml);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(InputStatus.Partial);
        result.HasUsableContent.Should().BeTrue();
        result.Deadlocks.Should().ContainSingle();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].Location!.EventIndex.Should().Be(2);
    }

    [Fact]
    public void BatchScan_ReportsInvalidFileAndContinuesToValidFile()
    {
        Write("bad.sqlplan", "<notAPlan/>");
        Write("good.sqlplan", Fixture("plan_no_relop.sqlplan"));
        var result = RunCli("--path", _directory, "--format", "json");
        result.Code.Should().Be(1);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.EnumerateArray().Select(e => e.GetProperty("Status").GetString())
            .Should().BeEquivalentTo("Failed", "Passed");
    }

    [Theory]
    [InlineData("json")]
    [InlineData("console")]
    [InlineData("junit")]
    public void ScanFormats_AllRejectUnrelatedXml(string format)
    {
        var result = RunCli("--path", Write("bad.sqlplan", "<notAPlan/>"), "--format", format);
        result.Code.Should().Be(1);
        result.Output.Should().Contain("INPUT_UNRELATED_XML").And.NotContain("Passed");
        if (format == "junit")
            SafeXmlHelper.ParseSafe(result.Output).Descendants("failure").Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refactor_WithInvalidAuxiliaryPlan_ReturnsFailureWithoutWriteback(bool dryRun)
    {
        string sql = Write("source.sql", "SELECT * FROM T WHERE Age + 10 > 50;");
        byte[] before = File.ReadAllBytes(sql);
        string plan = Write("invalid.sqlplan", "<notAPlan/>");
        var args = new List<string> { "refactor", sql, "--plan", plan, "--format", "json" };
        if (dryRun) args.Add("--dry-run");
        var result = RunCli(args.ToArray());
        result.Code.Should().Be(1);
        result.Output.Should().Contain("INPUT_UNRELATED_XML");
        File.ReadAllBytes(sql).Should().Equal(before);
        Directory.GetFiles(_directory).Should().HaveCount(2);
    }

    [Fact]
    public async Task KnownReadFailuresAndCancellation_DoNotGenerateDumps()
    {
        var reporter = new RecordingReporter();
        foreach (Exception error in new Exception[] { new IOException(), new UnauthorizedAccessException(), new System.Xml.XmlException() })
        {
            var reader = new InputRecognitionService(reporter, loadXml: _ => throw error);
            var result = reader.Load("unused");
            result.IsSuccess.Should().BeFalse();
            result.Status.Should().NotBe(InputStatus.UnexpectedError);
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = await new DocumentOpenService(new InputRecognitionService(reporter))
            .OpenAsync("missing", cancellation.Token);
        canceled.Status.Should().Be(InputStatus.Cancelled);
        reporter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refactor_MissingExplicitPlan_FailsWithoutGeneratingCandidateOrBackup(bool dryRun)
    {
        string sql = Write("source.sql", "SELECT * FROM T WHERE Age + 10 > 50;");
        var args = new List<string> { "refactor", sql, "--plan", Path.Combine(_directory, "missing.sqlplan"), "--format", "json" };
        if (dryRun) args.Add("--dry-run");
        var result = RunCli(args.ToArray());
        result.Code.Should().Be(1);
        result.Output.Should().Contain("INPUT_READ_ERROR");
        File.ReadAllText(sql).Should().Be("SELECT * FROM T WHERE Age + 10 > 50;");
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public void DesktopCli_ReportsEveryXmlEvent()
    {
        string path = Write("events.xdl", Fixture("deadlock_multiple_events.xdl"));
        TextWriter original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            CliService.RunAnalysis(path, null, null).Should().BeTrue();
        }
        finally { Console.SetOut(original); }
        output.ToString().Should().Contain("=== 死锁事件 1").And.Contain("=== 死锁事件 2");
    }

    [Fact]
    public void DesktopCli_BatchCountsInvalidInputAsFailureAndContinues()
    {
        Write("bad.sqlplan", "<notAPlan/>");
        Write("good.sqlplan", Fixture("plan_no_relop.sqlplan"));
        TextWriter original = Console.Out;
        int previousCode = Environment.ExitCode;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Environment.ExitCode = 0;
            CliService.RunBatchAnalysis(_directory, null, null);
            Environment.ExitCode.Should().Be(1);
        }
        finally { Console.SetOut(original); Environment.ExitCode = previousCode; }
        output.ToString().Should().Contain("成功: 1, 失败: 1");
    }

    [Fact]
    public void DesktopCli_InvalidInputDoesNotWriteOutputReport()
    {
        string output = Path.Combine(_directory, "report.txt");
        CliService.RunAnalysis(Write("bad.xml", "<notAPlan/>"), "txt", output).Should().BeFalse();
        File.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopCli_InvalidInput_PropagatesFailureThroughWpfShutdown(bool batch)
    {
        string path = Write("bad.sqlplan", "<notAPlan/>");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        string testAssembly = typeof(InputRecognitionTests).Assembly.Location;
        foreach (string argument in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"),
                     "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"), typeof(CliService).Assembly.Location,
                     "--analyze", batch ? _directory : path })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        process.ExitCode.Should().Be(1, await stderr);
        (await stderr).Should().Contain("INPUT_UNRELATED_XML");
        if (batch) (await stdout).Should().Contain("成功: 0, 失败: 1");
    }

    [Fact]
    public async Task CancellationDuringRead_ReturnsCancelledWithoutDocumentOrDump()
    {
        using var cancellation = new CancellationTokenSource();
        var reporter = new RecordingReporter();
        var reader = new InputRecognitionService(reporter, loadXml: _ =>
        {
            cancellation.Cancel();
            return SafeXmlHelper.ParseSafe(Deadlock);
        });
        var service = new DocumentOpenService(reader);
        var result = await service.OpenAsync(Write("exists.xml", Deadlock), cancellation.Token);
        result.Status.Should().Be(InputStatus.Cancelled);
        result.Document.Should().BeNull();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task UnknownReadFailure_CreatesRealDumpAndSidecar_AndReusesIncidentAcrossEntries()
    {
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), Path.Combine(_directory, "dumps"));
        var exception = new InvalidOperationException("IMP-06 synthetic unknown read failure");
        var reader = new InputRecognitionService(reporter, loadXml: _ => throw exception, parseXml: _ => throw exception);
        DocumentOpenResult gui = await new DocumentOpenService(reader).OpenAsync(Write("exists.xml", Deadlock));
        gui.IsSuccess.Should().BeFalse();
        gui.Status.Should().Be(InputStatus.UnexpectedError);
        gui.ErrorMessage.Should().Contain("DUMP：").And.NotContain("DUMP 生成失败");
        AnalysisReport analysis = new SqlXmlAnalysisEngine(recognition: reader).Analyze(Deadlock);
        analysis.IsSuccess.Should().BeFalse();
        analysis.InputErrorMessage.Should().Be(gui.ErrorMessage);
        var incident = reporter.Report(exception, "already captured");
        incident.DumpCreated.Should().BeTrue(incident.Failure);
        MinidumpValidator.Validate(incident.DumpPath!);
        File.ReadAllText(incident.MetadataPath!).Should().Contain("InputRecognition.Read");
        Directory.GetDirectories(Path.Combine(_directory, "dumps")).Should().ContainSingle();
    }

    [Fact]
    public void UnknownFailure_WhenDumpWriterFails_ReportsCaptureFailureWithoutSuccess()
    {
        var reporter = new UnexpectedErrorReporter(new FailingDumpWriter(), Path.Combine(_directory, "dumps"));
        var result = new InputRecognitionService(reporter, parseXml: _ => throw new InvalidOperationException("test"))
            .Parse("<any/>");
        result.Status.Should().Be(InputStatus.UnexpectedError);
        result.ErrorMessage.Should().Contain("DUMP 生成失败");
    }

    [Fact]
    public async Task Utf16BomWithMismatchedDeclaration_UsesSharedSafeFallback()
    {
        string path = Path.Combine(_directory, "utf16.sqlplan");
        File.WriteAllText(path, Fixture("plan_no_relop.sqlplan"), Encoding.Unicode);
        var result = await new DocumentOpenService().OpenAsync(path);
        result.IsSuccess.Should().BeTrue();
        RunCli("--path", path, "--format", "json").Code.Should().Be(0);
    }

    internal static string Fixture(string suffix)
    {
        var assembly = typeof(InputRecognitionTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AssertMatchesShowPlanSchema(XDocument document)
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };
        using var reader = SafeXmlHelper.ParseSafe(Fixture("showplanxml-sql2019.xsd")).CreateReader();
        schemas.Add(Ns, reader);
        var errors = new List<string>();
        document.Validate(schemas, (_, error) => errors.Add(error.Message));
        errors.Should().BeEmpty("fixture must match the vendored Microsoft ShowPlan XSD");
    }

    private string Write(string name, string contents)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static (int Code, string Output) RunCli(params string[] args)
    {
        TextWriter original = Console.Out;
        using var output = new StringWriter();
        try { Console.SetOut(output); return (Program.Main(args), output.ToString()); }
        finally { Console.SetOut(original); }
    }

    private sealed class RecordingReporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Calls++;
            return new(null, null, "recorded");
        }
    }

    private sealed class FailingDumpWriter : ICrashDumpWriter
    {
        public void Write(string path) => throw new IOException("synthetic disk full");
    }

    public void Dispose()
    {
        string path = Path.GetFullPath(_directory);
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp06Tests-");
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup target");
        Directory.Delete(path, recursive: true);
    }
}
