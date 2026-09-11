using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Privacy;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests.Privacy;

public sealed class OutputEntryPrivacyTests : IDisposable
{
    private const string Secret = PlanRedactionServiceTests.Marker;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-Imp05Outputs-" + Guid.NewGuid().ToString("N"));
    public OutputEntryPrivacyTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ClipboardEntries_ClearlyDistinguishRawDiagnosticsAndExecutableSql()
    {
        var clipboard = new AnalysisClipboardService();
        foreach (int tab in new[] { 0, 1 })
            clipboard.BuildForTab(tab, Secret, Secret).Text.Should().Contain(Secret).And.Contain(OutputPrivacy.RawNotice);
        clipboard.BuildRefactoredSql("SELECT '" + Secret + "';").Text.Should().Be("SELECT '" + Secret + "';");
        var ddl = new MissingIndexClipboardActionService();
        foreach (var value in new[] { ddl.BuildCreateScript(Secret), ddl.BuildRollbackScript(Secret) })
        {
            value.Text.Should().Be(Secret);
            value.SuccessMessage.Should().Contain(OutputPrivacy.RawNotice);
        }
        string node = new PlanGraphNodeClipboardService().BuildNodeInfo(new("1", "Table Scan", "Table Scan", 1, 100, "1", "1", "1", Secret, Secret, Secret, Secret, Secret));
        node.Should().Contain(Secret).And.Contain(OutputPrivacy.RawNotice);
    }

    [Fact]
    public void HtmlAndMermaid_KeepRawLabelEvenWhenOnlyDiagramContainsSensitiveValues()
    {
        string html = HtmlReportGenerator.GenerateReport("raw.sqlplan", "ExecutionPlan", Secret, "flowchart TD\n A[" + Secret + "]");
        html.Should().Contain(Secret).And.Contain(OutputPrivacy.RawNotice);
        BrowserLauncher.CreateMermaidHtml("flowchart TD\n A[" + Secret + "]", "test").Should().Contain(Secret).And.Contain(OutputPrivacy.RawNotice);
        new MermaidDiagramActionService().BuildPlanDiagram(PlanRedactionServiceTests.Fixture(), PlanRedactionService.ShowPlanNamespace)
            .MermaidCode.Should().Contain(Secret).And.Contain(OutputPrivacy.RawNotice);
    }

    [Fact]
    public void WordExport_ActualArchiveContainsRawNoticeAndDoesNotClaimRedaction()
    {
        string path = Path.Combine(_directory, "report.docx");
        ReportExportService.ExportToWord(path, "Report", Secret);
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = SafeXmlHelper.ParseSafe(reader.ReadToEnd());
        string text = string.Concat(xml.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
        text.Should().Contain(Secret).And.Contain(OutputPrivacy.RawNotice);
    }

    [Fact]
    public void JsonAndTextRefactorReports_RetainPrivacyStateWithSqlIncluded()
    {
        var result = new RefactorResult("SELECT '" + Secret + "';", true, Array.Empty<string>(), new RefactorContext("SELECT '" + Secret + "';"));
        string json = JsonSerializer.Serialize(new RefactorReportDto { OriginalSql = result.OutputSql });
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("Privacy").GetString().Should().Be(OutputPrivacy.RawNotice);
        document.RootElement.GetProperty("OriginalSql").GetString().Should().Contain(Secret);
        string output = Path.Combine(_directory, "refactor.txt");
        new ConsoleResultReporter { ShowSql = true }.Report(result, true, output);
        File.ReadAllText(output).Should().Contain(Secret).And.Contain(OutputPrivacy.RawNotice);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("docx")]
    public void PortableSharingRequest_IsBlockedBeforeDialogImageOrWriter(string extension)
    {
        var dialog = new Dialog(null);
        var exporter = new Exporter();
        var service = new PortableReportExportService(exporter, dialog);
        var result = service.Export(new(extension, "filter", new("title", Secret, "report." + extension, true), null, RequireRedacted: true));
        result.Status.Should().Be(PortableReportExportStatus.Blocked);
        dialog.Calls.Should().Be(0); exporter.Calls.Should().Be(0);
    }

    [Fact]
    public void HtmlSharingRequest_IsBlockedBeforeDialogOrWriter()
    {
        var dialog = new Dialog(null); var writer = new HtmlWriter();
        var report = new HtmlAnalysisReport("raw", "ExecutionPlan", Secret, Secret, Array.Empty<HtmlReportSection>(), "report.html", Array.Empty<MissingIndexSuggestion>());
        new HtmlReportExportService(writer, dialog).Export(new(report, RequireRedacted: true)).Status.Should().Be(HtmlReportExportStatus.Blocked);
        dialog.Calls.Should().Be(0); writer.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("docx")]
    public void PortableRawExport_PassesNoticeToRenderer(string extension)
    {
        var exporter = new Exporter();
        new PortableReportExportService(exporter, new Dialog(Path.Combine(_directory, "report." + extension)))
            .Export(new(extension, "filter", new("title", Secret, "report." + extension, true), null));
        exporter.Content.Should().Contain(Secret).And.Contain(OutputPrivacy.RawNotice);
    }

    [Fact]
    public void TuningSession_IsExplicitlyRawAndKeepsOriginalPlanForLocalReuse()
    {
        var service = new TuningSessionService();
        var snapshot = service.CaptureSnapshot(PlanRedactionServiceTests.Fixture(), "input.sqlplan", 1);
        string output = Path.Combine(_directory, "session.pesession");
        service.Save(output, new[] { snapshot }, snapshot, null);
        var document = SafeXmlHelper.LoadSafe(output);
        document.Root!.Attribute("Privacy")!.Value.Should().Be(OutputPrivacy.RawNotice);
        document.ToString().Should().Contain(Secret);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuiRedactionEntry_UsesSafePayloadOrBlocksBeforeSaveDialog(bool unknown)
    {
        var input = PlanRedactionServiceTests.Fixture();
        if (unknown) input.Root!.SetAttributeValue("UnknownSecretField", Secret);
        string before = input.ToString();
        string path = Path.Combine(_directory, "redacted.sqlplan");
        var dialog = new Dialog(path);
        var messages = new List<string>();
        var service = new PlanObfuscationExportUiActionService(dialog, showMessage: (message, title, buttons, icon) => messages.Add(message));
        service.Export(input, _ => { });
        input.ToString().Should().Be(before);
        string.Join("\n", messages).Should().NotContain(Secret);
        if (unknown) { dialog.Calls.Should().Be(0); File.Exists(path).Should().BeFalse(); }
        else { dialog.Calls.Should().Be(1); File.ReadAllText(path).Should().NotContain(Secret); messages[0].Should().Contain("处理类别"); }
    }

    [Fact]
    public void GuiRedaction_CancelledSaveDoesNotWrite()
    {
        var writer = new NoWrite();
        var service = new PlanObfuscationExportUiActionService(new Dialog(null), new RedactedPlanExportService(writer), (_, _, _, _) => { });
        service.Export(PlanRedactionServiceTests.Fixture(), _ => { });
        writer.Calls.Should().Be(0);
    }

    private sealed class Dialog(string? path) : IFileDialogService
    {
        public int Calls { get; private set; }
        public string? ShowOpenFile(FileDialogRequest request) => throw new NotSupportedException();
        public string? ShowSaveFile(FileDialogRequest request) { Calls++; return path; }
    }
    private sealed class Exporter : IPdfWordReportExporter
    {
        public int Calls { get; private set; }
        public string Content { get; private set; } = "";
        public void Export(string extension, string path, string title, string content, System.Windows.FrameworkElement? imageElement = null) { Calls++; Content = content; }
    }
    private sealed class HtmlWriter : IHtmlReportWriter
    {
        public int Calls { get; private set; }
        public string Save(HtmlAnalysisReport report, string path) { Calls++; return path; }
    }
    private sealed class NoWrite : IRedactedPlanWriter
    {
        public int Calls { get; private set; }
        public void WriteNew(string path, byte[] bytes, CancellationToken token) => Calls++;
    }
    public void Dispose()
    {
        string path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp05Outputs-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup target");
        Directory.Delete(path, true);
    }
}
