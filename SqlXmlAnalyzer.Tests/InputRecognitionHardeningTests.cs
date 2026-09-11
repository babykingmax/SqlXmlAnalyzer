using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using FluentAssertions;
using SqlXmlAnalyzer.Analysis;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class InputRecognitionHardeningTests : IDisposable
{
    private const string Ns = InputRecognitionService.ShowPlanNamespace;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp06Hardening-{Guid.NewGuid():N}");

    public InputRecognitionHardeningTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("QueryPlan", "urn:wrong")]
    [InlineData("RelOp", "urn:wrong")]
    [InlineData("RelOp", "")]
    [InlineData("IndexScan", "urn:wrong")]
    [InlineData("Object", "urn:wrong")]
    public async Task NestedNamespaceMismatch_AllEntriesRejectInsteadOfReportingNoOperators(string nodeName, string wrongNamespace)
    {
        XDocument document = SafeXmlHelper.ParseSafe(InputRecognitionTests.Fixture("plan_clean.sqlplan"));
        document.Descendants(XName.Get(nodeName, Ns)).Single().Name = XName.Get(nodeName, wrongNamespace);
        string path = Write("wrong.sqlplan", document.ToString());
        var gui = await new DocumentOpenService().OpenAsync(path);
        gui.IsSuccess.Should().BeFalse();
        gui.ErrorCode.Should().Be("INPUT_PLAN_STRUCTURE");
        new SqlXmlAnalysisEngine().Analyze(document.ToString()).InputErrorCode.Should().Be(gui.ErrorCode);
        var scan = RunCli("--path", path, "--block-scans", "--max-cost", "1", "--format", "json");
        scan.Code.Should().Be(1);
        scan.Output.Should().Contain("INPUT_PLAN_STRUCTURE").And.NotContain("Passed");
        AssertRefactorRejected(path, "INPUT_PLAN_STRUCTURE");
    }

    [Theory]
    [InlineData("<Unexpected/>")]
    [InlineData("<BatchSequence xmlns='urn:wrong'/>")]
    public void AdditionalRootChildren_AreRejected(string extra)
    {
        XDocument document = SafeXmlHelper.ParseSafe(InputRecognitionTests.Fixture("plan_no_relop.sqlplan"));
        document.Root!.Add(SafeXmlHelper.ParseSafe(extra).Root);
        InputRecognitionService.Recognize(document).ErrorCode.Should().Be("INPUT_PLAN_STRUCTURE");
    }

    [Fact]
    public void InternalInfo_ArbitraryNamespacedContentsRemainSupportedWithoutMutatingSource()
    {
        XDocument document = SafeXmlHelper.ParseSafe(InputRecognitionTests.Fixture("plan_clean.sqlplan"));
        document.Descendants(XName.Get("QueryPlan", Ns)).Single().Add(
            new XElement(XName.Get("InternalInfo", Ns),
                new XElement(XName.Get("RelOp", "urn:extension"),
                    new XElement("Anything", "opaque information"))));
        string before = document.ToString();
        InputRecognitionService.Recognize(document).IsSuccess.Should().BeTrue();
        document.ToString().Should().Be(before);
    }

    public static TheoryData<string, Encoding, byte[]> CorruptEncodings => new()
    {
        { "utf8", new UTF8Encoding(false), new byte[] { 0xFF } },
        { "utf8-bom", new UTF8Encoding(true), new byte[] { 0xFF } },
        { "utf16-le", new UnicodeEncoding(false, true), new byte[] { 0x00, 0xD8 } },
        { "utf16-be", new UnicodeEncoding(true, true), new byte[] { 0xD8, 0x00 } },
        { "utf32-le", new UTF32Encoding(false, true), new byte[] { 0x00, 0x00, 0x11, 0x00 } },
        { "utf32-be", new UTF32Encoding(true, true), new byte[] { 0x00, 0x11, 0x00, 0x00 } }
    };

    [Theory]
    [MemberData(nameof(CorruptEncodings))]
    public async Task InvalidEncodedBytes_AllFileEntriesFailWithoutReplacementOrDump(string name, Encoding encoding, byte[] badBytes)
    {
        string xml = InputRecognitionTests.Fixture("plan_no_relop.sqlplan").Replace("CREATE TABLE #imp06 (Id int);", "SELECT N'BYTE_MARKER';");
        int marker = xml.IndexOf("BYTE_MARKER", StringComparison.Ordinal);
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml[..marker]))
            .Concat(badBytes).Concat(encoding.GetBytes(xml[(marker + "BYTE_MARKER".Length)..])).ToArray();
        string path = Path.Combine(_directory, name + ".sqlplan");
        File.WriteAllBytes(path, bytes);
        var reporter = new RecordingReporter();
        var gui = await new DocumentOpenService(new InputRecognitionService(reporter)).OpenAsync(path);
        gui.IsSuccess.Should().BeFalse();
        gui.Document.Should().BeNull();
        gui.Status.Should().BeOneOf(InputStatus.MalformedXml, InputStatus.ReadError);
        reporter.Calls.Should().Be(0);
        var scan = RunCli("--path", path, "--format", "json");
        scan.Code.Should().Be(1);
        scan.Output.Should().Contain(gui.ErrorCode!);
        AssertRefactorRejected(path, gui.ErrorCode!);
    }

    public static TheoryData<Encoding> XmlEncodings => new()
    {
        new UTF8Encoding(false, true),
        new UnicodeEncoding(false, false, true),
        new UnicodeEncoding(true, false, true),
        new UTF32Encoding(false, false, true),
        new UTF32Encoding(true, false, true),
        new UTF32Encoding(false, true, true)
    };

    [Theory]
    [MemberData(nameof(XmlEncodings))]
    public async Task DeclaredEncoding_RealFilesAgreeAcrossGuiScanAndRefactor(Encoding encoding)
    {
        string text = InputRecognitionTests.Fixture("plan_no_relop.sqlplan")
            .Replace("encoding=\"utf-8\"", $"encoding=\"{encoding.WebName}\"")
            .Replace("CREATE TABLE #imp06 (Id int);", "SELECT N'中文é';");
        string path = Path.Combine(_directory, "encoded.sqlplan");
        File.WriteAllText(path, text, encoding);
        XDocument document = SafeXmlHelper.LoadSafe(path);
        var schemas = new XmlSchemaSet { XmlResolver = null };
        using var schemaReader = SafeXmlHelper.ParseSafe(InputRecognitionTests.Fixture("showplanxml-sql2019.xsd")).CreateReader();
        schemas.Add(Ns, schemaReader);
        document.Validate(schemas, null);
        var gui = await new DocumentOpenService().OpenAsync(path);
        gui.IsSuccess.Should().BeTrue();
        gui.Document!.ToString().Should().Contain("中文é").And.NotContain("\uFFFD");
        RunCli("--path", path, "--format", "json").Code.Should().Be(0);
        string sql = Write("source.sql", "SELECT 1;");
        var refactor = RunCli("refactor", sql, "--plan", path, "--dry-run", "--format", "json");
        refactor.Code.Should().Be(0, refactor.Output);
        File.ReadAllText(sql).Should().Be("SELECT 1;");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ActualBom_WithMismatchedDeclaration_RemainsCompatible(bool bigEndian, bool utf32)
    {
        Encoding encoding = utf32 ? new UTF32Encoding(bigEndian, true, true) : new UnicodeEncoding(bigEndian, true, true);
        string path = Path.Combine(_directory, "bom.sqlplan");
        File.WriteAllText(path, InputRecognitionTests.Fixture("plan_no_relop.sqlplan"), encoding);
        new InputRecognitionService().Load(path).IsSuccess.Should().BeTrue();
        string sql = Write("source.sql", "SELECT 1;");
        RunCli("refactor", sql, "--plan", path, "--dry-run", "--format", "json").Code.Should().Be(0);
    }

    [Fact]
    public void BomRetry_StillRejectsDtdWithoutResolvingExternalEntities()
    {
        string path = Path.Combine(_directory, "dtd.sqlplan");
        File.WriteAllText(path, "<?xml version='1.0' encoding='utf-8'?><!DOCTYPE a [<!ENTITY secret SYSTEM 'file:///must-not-read'>]><a>&secret;</a>", new UnicodeEncoding(false, true, true));
        new InputRecognitionService().Load(path).ErrorCode.Should().Be("INPUT_MALFORMED_XML");
    }

    [Fact]
    public void BomRetry_UsesOneHandleHandlesShortReadsAndDisposesStream()
    {
        var encoding = new UnicodeEncoding(false, true, true);
        var stream = new ShortReadStream(encoding.GetPreamble()
            .Concat(encoding.GetBytes(InputRecognitionTests.Fixture("plan_no_relop.sqlplan"))).ToArray());
        int opens = 0;
        var result = new InputRecognitionService(openRead: _ => { opens++; return stream; }).Load("synthetic.sqlplan");
        result.IsSuccess.Should().BeTrue();
        opens.Should().Be(1, "BOM retry must not reopen a potentially replaced path");
        stream.CanRead.Should().BeFalse();
    }

    [Fact]
    public void ByteStreamFactory_CancellationAfterOpenDisposesStreamWithoutDump()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(InputRecognitionTests.Fixture("plan_no_relop.sqlplan")));
        using var cancellation = new CancellationTokenSource();
        var reporter = new RecordingReporter();
        var service = new InputRecognitionService(reporter, openRead: _ => { cancellation.Cancel(); return stream; });
        Action read = () => service.Load("synthetic.sqlplan", cancellation.Token);
        read.Should().Throw<OperationCanceledException>();
        stream.CanRead.Should().BeFalse();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void ByteStreamFactory_UnknownErrorProducesValidatedDumpAndStableFailure()
    {
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), Path.Combine(_directory, "dumps"));
        var error = new InvalidOperationException("IMP-06 synthetic stream factory failure");
        var service = new InputRecognitionService(reporter, openRead: _ => throw error);
        var result = service.Load("synthetic.sqlplan");
        result.Status.Should().Be(InputStatus.UnexpectedError);
        result.ErrorCode.Should().Be("INPUT_UNEXPECTED_ERROR");
        result.Document.Should().BeNull();
        var incident = reporter.Report(error, "already captured");
        incident.DumpCreated.Should().BeTrue(incident.Failure);
        MinidumpValidator.Validate(incident.DumpPath!);
        File.ReadAllText(incident.MetadataPath!).Should().Contain("InputRecognition.Read");
        Directory.GetDirectories(Path.Combine(_directory, "dumps")).Should().ContainSingle();
    }

    [Fact]
    public void NestedStatementBlock_RejectsUnknownStatementKinds()
    {
        string xml = $"<ShowPlanXML xmlns='{Ns}'><BatchSequence><Batch><Statements><StmtCond><Condition/><Then><Statements><UnknownStatement/></Statements></Then></StmtCond></Statements></Batch></BatchSequence></ShowPlanXML>";
        new InputRecognitionService().Parse(xml).ErrorCode.Should().Be("INPUT_PLAN_STRUCTURE");
    }

    [Fact]
    public void TruncatedUtf16WithBom_IsAReadFailureWithoutReplacement()
    {
        var encoding = new UnicodeEncoding(false, true, true);
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(InputRecognitionTests.Fixture("plan_no_relop.sqlplan"))).ToArray();
        string path = Path.Combine(_directory, "truncated.sqlplan");
        File.WriteAllBytes(path, bytes[..^1]);
        var result = new InputRecognitionService().Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Document.Should().BeNull();
        result.Status.Should().BeOneOf(InputStatus.ReadError, InputStatus.MalformedXml);
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
    }

    private void AssertRefactorRejected(string planPath, string errorCode)
    {
        string sql = Write("source.sql", "SELECT * FROM T WHERE Age + 10 > 50;");
        byte[] before = File.ReadAllBytes(sql);
        foreach (bool dryRun in new[] { true, false })
        {
            var args = new List<string> { "refactor", sql, "--plan", planPath, "--format", "json" };
            if (dryRun) args.Add("--dry-run");
            var result = RunCli(args.ToArray());
            result.Code.Should().Be(1);
            using var json = JsonDocument.Parse(result.Output);
            json.RootElement.GetProperty("ErrorMessage").GetString().Should().Contain(errorCode);
            json.RootElement.GetProperty("SourceWritten").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("RefactoredSql").ValueKind.Should().Be(JsonValueKind.Null);
            File.ReadAllBytes(sql).Should().Equal(before);
            Directory.GetFiles(_directory).Should().HaveCount(2, "rejected input must not create backups or candidates");
        }
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
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
            return new(null, null, "test");
        }
    }

    public void Dispose()
    {
        string path = Path.GetFullPath(_directory);
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp06Hardening-");
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup target");
        Directory.Delete(path, recursive: true);
    }
}
