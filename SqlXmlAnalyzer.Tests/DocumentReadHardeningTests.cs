using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class DocumentReadHardeningTests : IDisposable
{
    private const string Deadlock = "<deadlock><process-list><process id='p1'/></process-list><resource-list><keylock/></resource-list><future>é</future></deadlock>";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp09Hardening-{Guid.NewGuid():N}");

    public DocumentReadHardeningTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EmptyXePayload_KeepsItsDiagnosticAndDoesNotRenumberLaterEvents(int emptyIndex)
    {
        string xml = "<events>\n" + string.Join("\n", Enumerable.Range(1, 3).Select(index =>
            WrapEvent(index == emptyIndex ? "<deadlock-list> <!-- no graph --> </deadlock-list>" : Deadlock))) + "</events>";
        var reporter = new Reporter();
        var reader = new InputRecognitionService(reporter);
        string path = Write("events.xml", xml);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var results = new[] { reader.Parse(xml), await reader.ReadAsync(stream), (await new DocumentOpenService(reader).OpenAsync(path)).Input! };
        foreach (var result in results)
        {
            result.Status.Should().Be(InputStatus.Partial);
            result.IsSuccess.Should().BeFalse();
            result.Deadlocks.Select(e => e.Index).Should().Equal(Enumerable.Range(1, 3).Where(i => i != emptyIndex));
            var diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
            diagnostic.Code.Should().Be("INPUT_DEADLOCK_STRUCTURE");
            diagnostic.Message.Should().Contain("为空");
            diagnostic.Location!.EventIndex.Should().Be(emptyIndex);
            diagnostic.Location.XmlPath.Should().Contain($"event[{emptyIndex}]").And.EndWith("deadlock-list[1]");
            diagnostic.Location.Line.Should().Be(emptyIndex + 1);
        }
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void OnlyEmptyXePayloads_ReturnInvalidWithEverySourcePosition()
    {
        var result = new InputRecognitionService().Parse("<RingBufferTarget>" +
            WrapEvent("<deadlock-list/>") + WrapEvent("<deadlock-list/>") + "</RingBufferTarget>");
        result.Status.Should().Be(InputStatus.Invalid);
        result.Deadlocks.Should().BeEmpty();
        result.Diagnostics.Select(d => d.Location!.EventIndex).Should().Equal(1, 2);
    }

    [Fact]
    public void SourcePositions_CountExpandedNamesAndAreRebuiltForEveryRecognition()
    {
        XDocument document = SafeXmlHelper.ParseSafe("<deadlock-list xmlns:f='urn:future'>\n<unknown/><f:unknown/><unknown/>" + Deadlock + "</deadlock-list>");
        var before = InputRecognitionService.Recognize(document);
        before.Diagnostics.Select(d => d.Location!.XmlPath).Should().Equal(
            "/deadlock-list[1]/unknown[1]", "/deadlock-list[1]/{urn:future}unknown[1]", "/deadlock-list[1]/unknown[2]");
        document.Root!.AddFirst(new XElement("unknown"));
        var after = InputRecognitionService.Recognize(document);
        after.Deadlocks.Single().Index.Should().Be(5);
        after.Diagnostics.Last().Location!.XmlPath.Should().EndWith("unknown[3]");
        before.Diagnostics.Last().Location!.XmlPath.Should().EndWith("unknown[2]");
    }

    [Fact]
    public void CachedSourcePosition_StillHonorsSubsequentCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new DocumentSource(cancellation.Token);
        XElement node = SafeXmlHelper.ParseSafe("<events><event/><event/></events>").Root!.Elements().Last();
        source.Location(node).XmlPath.Should().Be("/events[1]/event[2]");
        cancellation.Cancel();
        Action locate = () => source.Location(node);
        locate.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void LargeSiblingCollection_CompletesWithinBoundAndKeepsFinalPosition()
    {
        const int count = 80_000;
        var document = SafeXmlHelper.ParseSafe("<deadlock-list>" + string.Concat(Enumerable.Repeat("<unknown/>", count)) + "</deadlock-list>");
        var timer = Stopwatch.StartNew();
        var result = InputRecognitionService.Recognize(document);
        timer.Stop();
        result.Diagnostics.Should().HaveCount(count);
        result.Diagnostics[^1].Location!.XmlPath.Should().EndWith($"unknown[{count}]");
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "source locations must not repeatedly scan all prior siblings");
    }

    [Fact]
    public async Task CancellationAfterXmlRead_DiscardsCollectionAndDoesNotDump()
    {
        var document = SafeXmlHelper.ParseSafe("<deadlock-list>" + string.Concat(Enumerable.Repeat("<unknown/>", 80_000)) + "</deadlock-list>");
        using var cancellation = new CancellationTokenSource();
        var reporter = new Reporter();
        var reader = new InputRecognitionService(reporter, loadXml: _ =>
        {
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(1));
            return document;
        });
        var result = await reader.ReadFileAsync("memory.xml", cancellationToken: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
        result.Status.Should().Be(InputStatus.Cancelled);
        result.Document.Should().BeNull();
        result.Envelope.Should().BeNull();
        result.Diagnostics.Should().BeEmpty();
        reporter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1100)]
    [InlineData(8192)]
    public async Task LongEncodingDeclaration_AllXmlEntriesRetainNonAsciiText(int padding)
    {
        string xml = "<?xml version='1.0'" + new string(' ', padding) + "encoding='iso-8859-1'?>" + Deadlock;
        byte[] bytes = Encoding.Latin1.GetBytes(xml);
        string path = Path.Combine(_directory, "latin1.xdl");
        File.WriteAllBytes(path, bytes);
        var reporter = new Reporter();
        var reader = new InputRecognitionService(reporter);
        using var stream = new MemoryStream(bytes);
        var results = new[] { reader.Parse(xml), await reader.ReadAsync(stream), await reader.ReadFileAsync(path),
            (await new DocumentOpenService(reader).OpenAsync(path)).Input! };
        foreach (var result in results)
        {
            result.Status.Should().Be(InputStatus.Success);
            result.Document!.Descendants("future").Single().Value.Should().Be("é");
        }
        SafeXmlHelper.LoadSafe(path).Descendants("future").Single().Value.Should().Be("é");
        results[1].SourceSnapshot.ToArray().Should().Equal(bytes);
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task LongPlanDeclaration_ReadScanAndRefactorUseTheSameEncoding()
    {
        string plan = InputRecognitionTests.Fixture("plan_no_relop.sqlplan");
        var document = SafeXmlHelper.ParseSafe(plan);
        document.Declaration = null;
        string xml = "<?xml version='1.0'" + new string(' ', 2000) + "encoding='iso-8859-1'?>" + document.ToString();
        string path = Path.Combine(_directory, "plan.sqlplan");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(xml));
        (await new DocumentOpenService().OpenAsync(path)).IsSuccess.Should().BeTrue();
        DocumentReadContractTests.RunCli("read", path).Code.Should().Be(0);
        DocumentReadContractTests.RunCli("--path", path, "--format", "json").Code.Should().Be(0);
        string sql = Write("query.sql", "SELECT 1;");
        DocumentReadContractTests.RunCli("refactor", sql, "--plan", path, "--dry-run", "--format", "json").Code.Should().Be(0);
        File.ReadAllText(sql).Should().Be("SELECT 1;");
    }

    [Theory]
    [InlineData("<?xml version='1.0' ", InputStatus.MalformedXml)]
    [InlineData("<?xml version='1.0' encoding='no-such-encoding'?>", InputStatus.MalformedXml)]
    public async Task InvalidLongDeclaration_RemainsAnExpectedInputFailure(string declaration, InputStatus status)
    {
        string xml = declaration.Replace("version='1.0'", "version='1.0'" + new string(' ', 8192)) + Deadlock;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var reporter = new Reporter();
        var result = await new InputRecognitionService(reporter).ReadAsync(stream);
        result.Status.Should().Be(status);
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task LongDeclaration_StillEnforcesCharacterBudgetWithoutDump()
    {
        string xml = "<?xml version='1.0'" + new string(' ', 8192) + "encoding='iso-8859-1'?>" + Deadlock;
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(xml));
        var reporter = new Reporter();
        var result = await new InputRecognitionService(reporter).ReadAsync(stream, new DocumentReadOptions { MaxXmlCharacters = 4096 });
        result.Status.Should().Be(InputStatus.TooLarge);
        result.ErrorMessage.Should().Contain("XmlCharacters");
        reporter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("scan")]
    [InlineData("redact")]
    [InlineData("refactor")]
    public void AlreadyCancelledCliCommand_Returns130AndCreatesNoOutput(string command)
    {
        string sql = Write("source.sql", "SELECT * FROM Users WHERE Age + 10 > 50;");
        string plan = Write("source.sqlplan", InputRecognitionTests.Fixture("plan_no_relop.sqlplan"));
        string output = Path.Combine(_directory, "output.xml");
        string[] args = command switch
        {
            "read" => new[] { "read", plan },
            "scan" => new[] { "--path", plan, "--output", output },
            "redact" => new[] { "redact", plan, "--output", output },
            _ => new[] { "refactor", sql, "--output", output }
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var run = typeof(Program).GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Static)!;
        int code = (int)run.Invoke(null, new object[] { args, cancellation.Token })!;
        code.Should().Be(130);
        File.ReadAllText(sql).Should().Be("SELECT * FROM Users WHERE Age + 10 > 50;");
        Directory.GetFiles(_directory).Should().HaveCount(2);
    }

    [Fact]
    public void RefactorChildCommand_ReceivesCancellationBeforeCreatingItsServices()
    {
        string sql = Write("source.sql", "SELECT * FROM Users WHERE Age + 10 > 50;");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = typeof(Program).GetMethod("HandleRefactorCommand", BindingFlags.NonPublic | BindingFlags.Static)!;
        int code = (int)handler.Invoke(null, new object[] { new[] { sql }, new DocumentReadOptions(), cancellation.Token })!;
        code.Should().Be(130);
        File.ReadAllText(sql).Should().Be("SELECT * FROM Users WHERE Age + 10 > 50;");
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    private static string WrapEvent(string payload) => $"<event name='xml_deadlock_report'><data name='xml_report'><value>{payload}</value></data></event>";
    private string Write(string name, string text)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private sealed class Reporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; return new(null, null, "test"); }
    }

    public void Dispose()
    {
        string full = Path.GetFullPath(_directory);
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp09Hardening-");
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(full, recursive: true);
    }
}
