using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.Analysis;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class DocumentReadContractTests : IDisposable
{
    private const string Deadlock = "<deadlock><process-list><process id='p1'/></process-list><resource-list><keylock future='retained'/></resource-list><future-field>unknown</future-field></deadlock>";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp09-{Guid.NewGuid():N}");
    public DocumentReadContractTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task NonSeekableStream_PreservesSnapshotHashLocationsUnknownFieldsAndOffset()
    {
        string xml = "<events>\n<event name='xml_deadlock_report' timestamp='2026-09-08T08:01:00+08:00' future='opaque'><data name='xml_report'><value>" + Deadlock + "</value></data><action name='unknown'>value</action></event></events>";
        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        using var stream = new ChunkedStream(bytes);
        var result = await new InputRecognitionService().ReadAsync(stream, sourceName: "capture.xml");
        result.Status.Should().Be(InputStatus.Success);
        result.Envelope!.SourceHash.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)));
        result.Envelope.SourceBytes.Should().Be(bytes.Length);
        result.Envelope.CapturedAt!.Value.Offset.Should().Be(TimeSpan.FromHours(8));
        result.SourceSnapshot.ToArray().Should().Equal(bytes);
        result.Deadlocks[0].Location!.Line.Should().Be(2);
        result.Deadlocks[0].Location!.XmlPath.Should().Contain("event[1]").And.Contain("deadlock[1]");
        result.Document!.ToString().Should().Contain("opaque").And.Contain("unknown");
        result.Deadlocks[0].OriginalElement!.ToString().Should().Contain("future-field");
        result.Capabilities.HasFlag(DocumentCapabilities.OffsetTimestamps).Should().BeTrue();
        stream.CanRead.Should().BeTrue("the caller owns the stream");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-09-08 08:00:00")]
    [InlineData("invalid")]
    public void MissingOrUnzonedTime_DoesNotInventCaptureTime(string? timestamp)
    {
        string xml = $"<event name='xml_deadlock_report' timestamp='{timestamp}'><data name='xml_report'><value>{Deadlock}</value></data></event>";
        var result = new InputRecognitionService().Parse(xml);
        result.Envelope!.CapturedAt.Should().BeNull();
        result.Deadlocks[0].CapturedAt.Should().BeNull();
        result.Deadlocks[0].Timestamp.Should().Be(timestamp ?? "");
        result.Capabilities.HasFlag(DocumentCapabilities.OffsetTimestamps).Should().BeFalse();
    }

    [Fact]
    public async Task PlanWithoutOperators_ReportsOnlyObservedCapabilitiesAndPreservesSourceIntoAnalysis()
    {
        string path = Write("plan.sqlplan", InputRecognitionTests.Fixture("plan_no_relop.sqlplan"));
        var input = await new InputRecognitionService().ReadFileAsync(path);
        input.Capabilities.Should().Be(DocumentCapabilities.PlanStatements | DocumentCapabilities.PreservedSource);
        var report = new SqlXmlAnalysisEngine().AnalyzeInput(input);
        report.IsSuccess.Should().BeTrue();
        report.InputEnvelope.Should().BeSameAs(input.Envelope);
        report.InputEnvelope!.EngineBuild.Should().Be(input.Document!.Root!.Attribute("Build")!.Value);
        report.InputEnvelope.SchemaVersion.Should().Be(input.Document.Root.Attribute("Version")!.Value);
        report.InputEnvelope.CapturedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("Bytes")]
    [InlineData("XmlCharacters")]
    [InlineData("Depth")]
    [InlineData("Nodes")]
    public async Task BudgetExceeded_GuiAndStreamAgreeAndDoNotDump(string budget)
    {
        var options = budget switch
        {
            "Bytes" => new DocumentReadOptions { MaxBytes = 16 },
            "XmlCharacters" => new DocumentReadOptions { MaxXmlCharacters = 16 },
            "Depth" => new DocumentReadOptions { MaxDepth = 2 },
            _ => new DocumentReadOptions { MaxNodes = 3 }
        };
        var reporter = new RecordingReporter();
        var reader = new InputRecognitionService(reporter, options: options);
        var gui = await new DocumentOpenService(reader).OpenAsync(Write("budget.xml", Deadlock));
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes(Deadlock));
        var streamed = await reader.ReadAsync(stream);
        foreach (var result in new[] { gui.Input!, streamed })
        {
            result.Status.Should().Be(InputStatus.TooLarge);
            result.ErrorCode.Should().Be("INPUT_TOO_LARGE");
            result.ErrorMessage.Should().Contain(budget);
            result.HasUsableContent.Should().BeFalse();
            result.Document.Should().BeNull();
            result.Deadlocks.Should().BeEmpty();
        }
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ExactByteAndCharacterLimits_AreAccepted()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Deadlock);
        using var stream = new ChunkedStream(bytes);
        var options = new DocumentReadOptions { MaxBytes = bytes.Length, MaxXmlCharacters = Deadlock.Length };
        (await new InputRecognitionService().ReadAsync(stream, options)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationWhileReading_ReturnsCancelledWithoutSnapshotOrDump()
    {
        using var cancellation = new CancellationTokenSource();
        var reporter = new RecordingReporter();
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes(Deadlock), afterRead: cancellation.Cancel);
        var result = await new InputRecognitionService(reporter).ReadAsync(stream, cancellationToken: cancellation.Token);
        result.Status.Should().Be(InputStatus.Cancelled);
        result.Document.Should().BeNull();
        result.Envelope.Should().BeNull();
        result.HasUsableContent.Should().BeFalse();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CancellationRacingStreamFailure_StillReturnsCancelledWithoutThrowing()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes(Deadlock), afterRead: () =>
        {
            cancellation.Cancel();
            throw new IOException("simultaneous stream failure");
        });
        var reporter = new RecordingReporter();
        var result = await new InputRecognitionService(reporter).ReadAsync(stream, cancellationToken: cancellation.Token);
        result.Status.Should().Be(InputStatus.Cancelled);
        result.HasUsableContent.Should().BeFalse();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task UnknownReadFailure_GeneratesValidatedDumpAndModeAppropriateLogs()
    {
        Logger.Shutdown();
        string log = Path.Combine(_directory, "read.log");
        Logger.Initialize(customLogFilePath: log);
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), Path.Combine(_directory, "dumps"));
        var error = new InvalidOperationException("IMP09 synthetic read failure");
        var reader = new InputRecognitionService(reporter, openRead: _ => throw error);
        var input = await reader.ReadFileAsync("synthetic.xml");
        input.Status.Should().Be(InputStatus.UnexpectedError);
        var incident = reporter.Report(error, "already-captured");
        incident.DumpCreated.Should().BeTrue(incident.Failure);
        MinidumpValidator.Validate(incident.DumpPath!);
        File.ReadAllText(incident.MetadataPath!).Should().Contain("InputRecognition.Read");
        new InputRecognitionService().Parse("<deadlock-list>" + Deadlock + "<unknown/></deadlock-list>");
        new InputRecognitionService().Parse("<unrelated/>");
        Logger.Shutdown();
        string text = File.ReadAllText(log);
        text.Should().Contain("[ERROR]").And.Contain("[CRITICAL]");
#if DEBUG
        text.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        text.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
    }

    [Fact]
    public async Task DumpProviderFailure_PreservesUnknownReadFailure()
    {
        var reader = new InputRecognitionService(new ThrowingReporter(), openRead: _ => throw new InvalidOperationException("original"));
        var input = await reader.ReadFileAsync("synthetic.xml");
        input.Status.Should().Be(InputStatus.UnexpectedError);
        input.ErrorMessage.Should().Contain("DUMP 生成失败");
    }

    [Fact]
    public void PartialCollection_KeepsSourceOrdinalsAndRejectsImplicitSingleEventAnalysis()
    {
        string xml = "<deadlock-list><unknown/>" + Deadlock + "<deadlock/>" + Deadlock + "</deadlock-list>";
        var input = new InputRecognitionService().Parse(xml);
        input.Status.Should().Be(InputStatus.Partial);
        input.Deadlocks.Select(e => e.Index).Should().Equal(2, 4);
        input.Diagnostics.Select(d => d.Location!.EventIndex).Should().Equal(1, 3);
        input.Document!.Root!.Elements().Should().HaveCount(4);
        input.ForExecutionPlan().IsSuccess.Should().BeFalse();
        DeadlockXmlParser.TryParseDeadlockXml(input.Document).IsSuccess.Should().BeFalse();
        DeadlockXmlParser.TryParseDeadlockXml(input.Deadlocks[1].Document).IsSuccess.Should().BeTrue();
        InputReadPresentation.Describe(input).Should().Contain("Partial").And.Contain("deadlock[2]");
    }

    [Theory]
    [InlineData("<!DOCTYPE deadlock [<!ENTITY x SYSTEM 'file:///must-not-read'>]><deadlock>&x;</deadlock>", InputStatus.MalformedXml)]
    [InlineData("<deadlock-list><deadlock></deadlock-list>", InputStatus.MalformedXml)]
    [InlineData("<unrelated/>", InputStatus.Unrecognized)]
    [InlineData("<ShowPlanXML xmlns='urn:future'/>", InputStatus.Unsupported)]
    public async Task InvalidAndUnsupportedStreams_ReturnDistinctStatuses(string xml, InputStatus expected)
    {
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes(xml));
        var result = await new InputRecognitionService().ReadAsync(stream);
        result.Status.Should().Be(expected);
        result.HasUsableContent.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void CliReadAndScan_ExposeContractAndHonorConfiguredBudget()
    {
        string path = Write("plan.sqlplan", InputRecognitionTests.Fixture("plan_no_relop.sqlplan"));
        var read = RunCli("read", path);
        read.Code.Should().Be(0);
        using var json = JsonDocument.Parse(read.Output);
        json.RootElement.GetProperty("Envelope").GetProperty("SourceHash").GetString().Should().NotBeNullOrEmpty();
        json.RootElement.GetProperty("Capabilities").GetString().Should().Contain("PlanStatements").And.NotContain("RuntimeCounters");
        string options = Write("limits.json", "{\"MaxBytes\":16}");
        var scan = RunCli("--path", path, "--format", "json", "--read-options", options);
        scan.Code.Should().Be(1);
        scan.Output.Should().Contain("TooLarge").And.Contain("INPUT_TOO_LARGE");
        var limitedRead = RunCli("read", path, "--read-options", options);
        limitedRead.Code.Should().Be(1);
        limitedRead.Output.Should().Contain("TooLarge");
    }

    [Fact]
    public void CliPartialRead_ExposesSkippedLocationsAndReturnsFailure()
    {
        var result = RunCli("read", Write("mixed.xdl", "<deadlock-list>" + Deadlock + "<unknown/></deadlock-list>"));
        result.Code.Should().Be(1);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("Status").GetString().Should().Be("Partial");
        json.RootElement.GetProperty("HasUsableContent").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("Diagnostics")[0].GetProperty("Location").GetProperty("EventIndex").GetInt32().Should().Be(2);
    }

    [Fact]
    public void BatchScan_ContinuesAfterBudgetFailureAndRefactorDoesNotWrite()
    {
        string good = Write("good.sqlplan", InputRecognitionTests.Fixture("plan_no_relop.sqlplan"));
        Write("oversize.sqlplan", new string(' ', 800) + File.ReadAllText(good));
        string limits = Write("limits.json", "{\"MaxBytes\":700}");
        var batch = RunCli("--path", _directory, "--format", "json", "--read-options", limits);
        batch.Code.Should().Be(1);
        using var json = JsonDocument.Parse(batch.Output);
        json.RootElement.EnumerateArray().Select(e => e.GetProperty("InputStatus").GetString())
            .Should().BeEquivalentTo("Success", "TooLarge");
        string sql = Write("source.sql", "SELECT 1;");
        var refactor = RunCli("refactor", sql, "--plan", Path.Combine(_directory, "oversize.sqlplan"), "--format", "json", "--read-options", limits);
        refactor.Code.Should().Be(1);
        refactor.Output.Should().Contain("INPUT_TOO_LARGE");
        File.ReadAllText(sql).Should().Be("SELECT 1;");
        Directory.GetFiles(_directory).Should().HaveCount(4, "input rejection must not create a candidate or backup");
    }

    [Theory]
    [InlineData("{\"MaxBytes\":0}")]
    [InlineData("{\"MaxDepth\":-1}")]
    [InlineData("{\"MisspelledBudget\":100}")]
    [InlineData("{")]
    public void CliInvalidBudget_ReturnsUsageError(string json)
    {
        RunCli("read", "unused", "--read-options", Write("options.json", json)).Code.Should().Be(2);
    }

    internal static (int Code, string Output) RunCli(params string[] args)
    {
        TextWriter original = Console.Out;
        using var output = new StringWriter();
        try { Console.SetOut(output); return (Program.Main(args), output.ToString()); }
        finally { Console.SetOut(original); }
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private sealed class ChunkedStream(byte[] bytes, Action? afterRead = null) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = base.Read(buffer.Span[..Math.Min(7, buffer.Length)]);
            afterRead?.Invoke();
            return ValueTask.FromResult(read);
        }
    }

    private sealed class RecordingReporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; return new(null, null, "test"); }
    }
    private sealed class ThrowingReporter : IUnexpectedErrorReporter
    {
        public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("sink unavailable");
    }

    public void Dispose()
    {
        Logger.Shutdown();
        string path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp09-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup target");
        Directory.Delete(path, recursive: true);
    }
}
