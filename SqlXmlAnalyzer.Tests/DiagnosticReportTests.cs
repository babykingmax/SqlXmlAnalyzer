using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Privacy;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Tests.Privacy;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class DiagnosticReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP22-" + Guid.NewGuid().ToString("N"));
    public DiagnosticReportTests() => Directory.CreateDirectory(_directory);
    internal static DiagnosticReport Report(XDocument? document = null)
    {
        document ??= PlanRedactionServiceTests.Fixture();
        var diagnostics = PlanDiagnosticAnalyzer.AnalyzeDetailed(document, document.Root!.Name.Namespace);
        return DiagnosticReportFactory.Plan(document, PlanIdentityAdapter.GetDocument(document)!, diagnostics);
    }

    [Fact]
    public void SelectedStatement_UsesSameIssuesAsWorkspaceAndPreservesOutputVersusReadRows()
    {
        var doc = SafeXmlHelper.ParseSafe($"""
            <ShowPlanXML xmlns="{PlanRedactionService.ShowPlanNamespace}" Version="1.571" Build="16.0.1"><BatchSequence><Batch><Statements>
            <StmtSimple StatementText="SELECT 'FIRST_SECRET'"><QueryPlan><RelOp NodeId="0" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="10" /></QueryPlan></StmtSimple>
            <StmtSimple StatementText="SELECT 'SECOND_SECRET'"><QueryPlan><RelOp NodeId="0" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="1"><RunTimeInformation><RunTimeCountersPerThread Thread="0" ActualRows="100" ActualRowsRead="1000" ActualExecutions="1" /></RunTimeInformation></RelOp></QueryPlan></StmtSimple>
            </Statements></Batch></BatchSequence></ShowPlanXML>
            """);
        var diagnostics = PlanDiagnosticAnalyzer.AnalyzeDetailed(doc, doc.Root!.Name.Namespace);
        var workspace = new PlanWorkspaceViewModel(); workspace.Open(doc, diagnostics, []); workspace.SelectedStatement = workspace.Statements[1];
        var report = workspace.CreateReport();
        report.Scope.Should().Be("B1/S2/Q1");
        report.Issues.Select(i => i.RuleId).Should().Equal(workspace.Issues.Where(i => i.Diagnostic != null).Select(i => i.Diagnostic!.RuleId));
        report.Facts.Single(f => f.Name == "B1/S2/Q1/O1/OutputRows/Value").Value.Should().Be("100");
        report.Facts.Single(f => f.Name == "B1/S2/Q1/O1/RowsRead/Value").Value.Should().Be("1000");
        report.SourceXml.Should().Contain("SECOND_SECRET").And.NotContain("FIRST_SECRET");
        report.Nodes.Should().ContainSingle(); report.Nodes[0].Id.Should().Be("B1/S2/Q1/O1");
        var preview = new ReportRedactionService().Preview(report);
        preview.CanExport.Should().BeTrue(preview.Summary);
        preview.Report!.Facts.Single(f => f.Name == "B1/S2/Q1/O1/OutputRows/State").Value.Should().Be("Available");
        preview.Report.Facts.Single(f => f.Name == "B1/S2/Q1/O1/OutputRows/Unit").Value.Should().Be("rows");
        preview.Report.Facts.Single(f => f.Name == "B1/S2/Q1/O1/RowsRead/Value").Value.Should().Be("1000");
    }

    [Fact]
    public void Snapshot_IsDetachedAndRejectsStaleRecreation()
    {
        var doc = PlanRedactionServiceTests.Fixture();
        var diagnostics = PlanDiagnosticAnalyzer.AnalyzeDetailed(doc, doc.Root!.Name.Namespace);
        var model = PlanIdentityAdapter.GetDocument(doc)!;
        var report = DiagnosticReportFactory.Plan(doc, model, diagnostics);
        string snapshot = DiagnosticReportRenderer.Json(report);
        doc.Root!.SetAttributeValue("Build", "17.0.0");
        DiagnosticReportRenderer.Json(report).Should().Be(snapshot);
        Action rebuild = () => DiagnosticReportFactory.Plan(doc, model, diagnostics);
        rebuild.Should().Throw<InvalidDataException>();
        report.Facts.Should().NotBeAssignableTo<ReportField[]>();
    }

    [Fact]
    public void NestedStatementXml_DoesNotIncludeAnotherSelectableStatement()
    {
        var doc = SafeXmlHelper.ParseSafe(PlanIdentityModelTests.Wrap("<StmtSimple StatementText='outer'><QueryPlan><RelOp NodeId='0'/></QueryPlan><UDF><Statements><StmtSimple StatementText='inner'><QueryPlan><RelOp NodeId='0'/></QueryPlan></StmtSimple></Statements></UDF></StmtSimple>"));
        var diagnostic = PlanDiagnosticAnalyzer.AnalyzeDetailed(doc, doc.Root!.Name.Namespace);
        var model = PlanIdentityAdapter.GetDocument(doc)!;
        var report = DiagnosticReportFactory.Plan(doc, model, diagnostic, model.Statements[0].Key);
        report.SourceXml.Should().Contain("outer").And.NotContain("inner");
        report.Nodes.Should().ContainSingle();
    }

    [Fact]
    public void Redaction_CoversAllPayloadSurfacesAndDoesNotExportBeforeSamples()
    {
        var source = PlanRedactionServiceTests.Fixture(); string before = source.ToString();
        var raw = Report(source); var preview = new ReportRedactionService().Preview(raw);
        preview.CanExport.Should().BeTrue(preview.Summary); preview.Counts.Should().ContainKey("GraphLabel");
        preview.Samples.Should().Contain(s => s.Before.Contains(PlanRedactionServiceTests.Marker));
        var redacted = preview.Report!;
        foreach (string output in new[] { DiagnosticReportRenderer.Text(redacted), DiagnosticReportRenderer.Json(redacted), DiagnosticReportRenderer.Html(redacted), DiagnosticReportRenderer.Svg(redacted) })
            output.Should().NotContain(PlanRedactionServiceTests.Marker);
        redacted.IssueCount.Should().Be(raw.IssueCount); redacted.FactCount.Should().Be(raw.FactCount);
        redacted.Issues.Select(i => (i.RuleId, i.Location, i.Status)).Should().Equal(raw.Issues.Select(i => (i.RuleId, i.Location, i.Status)));
        redacted.Issues.SelectMany(i => i.Fields).Where(f => f.Name == "Origins").Should().Equal(raw.Issues.SelectMany(i => i.Fields).Where(f => f.Name == "Origins"));
        source.ToString().Should().Be(before); raw.SourceXml.Should().Contain(PlanRedactionServiceTests.Marker);
    }

    [Theory]
    [InlineData("attribute")]
    [InlineData("element")]
    [InlineData("namespace")]
    public void UnknownXml_BlocksRedactedExportWithoutPartialPayload(string mode)
    {
        var doc = PlanRedactionServiceTests.Fixture();
        if (mode == "attribute") doc.Root!.SetAttributeValue(PlanRedactionServiceTests.Marker, "secret");
        else doc.Root!.Add(new XElement(mode == "namespace" ? XName.Get("Object", "urn:secret") : doc.Root.Name.Namespace + "Uncovered", "secret"));
        var vm = new ReportReviewViewModel(Report(doc));
        vm.Redaction.Unsupported.Values.Sum().Should().BeGreaterThan(0);
        vm.Redaction.Report.Should().BeNull(); vm.CanExport.Should().BeFalse();
        vm.Redaction.Summary.Should().NotContain(PlanRedactionServiceTests.Marker);
        vm.IsRedacted = false; vm.CanExport.Should().BeTrue(); vm.PrivacyNotice.Should().Be(OutputPrivacy.RawNotice);
    }

    [Fact]
    public void CancelledPreview_DoesNotReturnPayload()
    {
        var result = new ReportRedactionService().Preview(Report(), new CancellationToken(true));
        result.CanExport.Should().BeFalse(); result.Report.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HtmlAndJson_ContainExactlyTheSameCanonicalReport(bool redact)
    {
        var report = Report(); if (redact) report = new ReportRedactionService().Preview(report).Report!;
        string html = DiagnosticReportRenderer.Html(report);
        string payload = html.Split("<pre id=\"report-text\">")[1].Split("</pre>")[0];
        WebUtility.HtmlDecode(payload).Should().Be(DiagnosticReportRenderer.Text(report));
        using var json = JsonDocument.Parse(DiagnosticReportRenderer.Json(report));
        json.RootElement.GetProperty("IssueCount").GetInt32().Should().Be(report.IssueCount);
        json.RootElement.GetProperty("Facts").GetArrayLength().Should().Be(report.FactCount);
        html.Should().Contain("script-src 'none'").And.NotContain("<script").And.NotContain("cdn.jsdelivr.net");
    }

    [Theory]
    [InlineData("html")]
    [InlineData("json")]
    [InlineData("pdf")]
    [InlineData("docx")]
    [InlineData("svg")]
    [InlineData("sqlplan")]
    public void Export_PublishesCompleteNewArtifactsAndPreservesExistingFile(string format)
    {
        var report = new ReportRedactionService().Preview(Report()).Report!;
        string path = Path.Combine(_directory, "report." + format);
        var service = new DiagnosticReportExportService();
        var result = service.Export(report, format, path);
        result.Succeeded.Should().BeTrue(result.Message);
        byte[] bytes = File.ReadAllBytes(path); bytes.Length.Should().BeGreaterThan(20);
        service.Export(report, format, path).Succeeded.Should().BeFalse();
        File.ReadAllBytes(path).Should().Equal(bytes);
        Directory.GetFiles(_directory, ".tmp.report-*").Should().BeEmpty();
        if (format == "docx")
        {
            using var zip = ZipFile.OpenRead(path);
            using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            var xml = SafeXmlHelper.ParseSafe(reader.ReadToEnd());
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            string text = string.Join("\n", xml.Descendants(w + "p").Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value))));
            foreach (string line in DiagnosticReportRenderer.Text(report).Split('\n').Where(l => l.Trim().Length > 0)) text.Should().Contain(line.TrimEnd('\r'));
            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml")))
            {
                using var contents = new StreamReader(entry.Open()); contents.ReadToEnd().Should().NotContain(PlanRedactionServiceTests.Marker);
            }
            zip.Entries.Should().Contain(e => e.FullName.StartsWith("word/media/"));
        }
        else if (format == "pdf") System.Text.Encoding.ASCII.GetString(bytes[..5]).Should().Be("%PDF-");
        else File.ReadAllText(path).Should().NotContain(PlanRedactionServiceTests.Marker);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("io")]
    [InlineData("empty")]
    public void InterruptedExport_DoesNotPublishOrLeaveOwnedTemporaryFile(string mode)
    {
        string path = Path.Combine(_directory, "report.json"); var cancellation = new CancellationTokenSource();
        var writer = new Writer((_, _, temp, _) =>
        {
            if (mode == "empty") return;
            File.WriteAllText(temp, "partial");
            if (mode == "cancel") cancellation.Cancel(); else throw new IOException("synthetic write interruption");
        });
        var result = new DiagnosticReportExportService(writer).Export(Report(), "json", path, cancellation.Token);
        result.Succeeded.Should().BeFalse(); File.Exists(path).Should().BeFalse(); Directory.GetFiles(_directory).Should().BeEmpty();
    }

    [Fact]
    public async Task ExportLocksSnapshotAndFormatUntilWriterCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var source = Report(); DiagnosticReport? received = null;
        var writer = new Writer((report, format, path, token) => { received = report; entered.SetResult(); if (!release.Wait(TimeSpan.FromSeconds(10), token)) throw new TimeoutException(); File.WriteAllText(path, "complete"); });
        var vm = new ReportReviewViewModel(source, "json", writer: writer);
        Task pending = vm.ExportAsync(Path.Combine(_directory, "report.json"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            vm.CanEdit.Should().BeFalse(); vm.CanExport.Should().BeFalse();
            vm.IsRedacted = false; vm.Format = "html";
            vm.IsRedacted.Should().BeTrue(); vm.Format.Should().Be("json"); received.Should().BeSameAs(vm.Report);
        }
        finally { release.Set(); }
        await pending; vm.IsBusy.Should().BeFalse(); vm.Status.Should().Contain("完整保存");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadlockReport_UsesSelectedEventAndMasksSqlObjectsAndRawAttributeNames(bool unknownElement)
    {
        string fixture = Utilities.EmbeddedResourceHelper.GetResourceContent("deadlock_bookmark_lookup.xdl");
        var doc = SafeXmlHelper.ParseSafe(fixture);
        foreach (var attribute in doc.Descendants().Attributes().Where(a => a.Name.LocalName is "objectname" or "hostname" or "loginname")) attribute.Value = PlanRedactionServiceTests.Marker;
        doc.Descendants().First(e => e.Name.LocalName == "inputbuf").Value = "SELECT '" + PlanRedactionServiceTests.Marker + "'";
        doc.Descendants().First(e => e.Name.LocalName == "process").SetAttributeValue(PlanRedactionServiceTests.Marker, PlanRedactionServiceTests.Marker);
        if (unknownElement) doc.Root!.Add(new XElement("Uncovered", "secret"));
        var input = new InputRecognitionService().Parse(doc.ToString());
        var selected = input.Deadlocks.Single(); var analysis = new DeadlockAnalysisService().Analyze(selected.Document);
        var workspace = new DeadlockWorkspaceViewModel(); workspace.Begin(input, selected); workspace.Complete(selected.Document, analysis);
        var report = workspace.CreateReport(input.Envelope);
        report.Scope.Should().Be("Event" + selected.Index); report.IssueCount.Should().Be(analysis.Patterns.Count);
        string before = selected.Document.ToString();
        var redaction = new ReportRedactionService().Preview(report);
        redaction.CanExport.Should().Be(!unknownElement, redaction.Summary);
        if (!unknownElement)
        {
            DiagnosticReportRenderer.Json(redaction.Report!).Should().NotContain(PlanRedactionServiceTests.Marker);
            redaction.Report!.Nodes.Select(n => n.Id).Should().Equal(report.Nodes.Select(n => n.Id));
            redaction.Report.Edges.Should().Equal(report.Edges);
            var locations = report.Issues.SelectMany(i => i.Fields).Where(f => f.Name.EndsWith("/Location")).ToArray();
            locations.Should().NotBeEmpty();
            redaction.Report.Issues.SelectMany(i => i.Fields).Where(f => f.Name.EndsWith("/Location")).Should().Equal(locations);
        }
        selected.Document.ToString().Should().Be(before);
        selected.Document.Root!.Add(new XElement("changed"));
        Action recreate = () => workspace.CreateReport(input.Envelope); recreate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void InvalidScopeAndMismatchedSource_AreRejected()
    {
        var doc = PlanRedactionServiceTests.Fixture(); var diagnostics = PlanDiagnosticAnalyzer.AnalyzeDetailed(doc, doc.Root!.Name.Namespace);
        var model = PlanIdentityAdapter.GetDocument(doc)!;
        Action wrong = () => DiagnosticReportFactory.Plan(doc, model, diagnostics, new PlanStatementKey(model.Batches[0].Key, 999));
        wrong.Should().Throw<InvalidDataException>();
        var other = PlanRedactionServiceTests.Fixture(); other.Root!.SetAttributeValue("Build", "17.0.0");
        var otherDiagnostics = PlanDiagnosticAnalyzer.AnalyzeDetailed(other, other.Root.Name.Namespace);
        Action mismatch = () => DiagnosticReportFactory.Plan(doc, model, otherDiagnostics); mismatch.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void CliScan_ExposesSameUnifiedDocumentReport()
    {
        string path = Path.Combine(_directory, "plan.sqlplan"); File.WriteAllText(path, PlanRedactionServiceTests.Fixture().ToString());
        var scan = CLI.Program.ScanPlanFile(path, null, null, false, new(), default);
        scan.DiagnosticReport.Should().NotBeNull();
        scan.DiagnosticReport!.Scope.Should().Be("Document");
        scan.DiagnosticReport.Issues.Select(i => i.RuleId).Should().Equal(scan.Diagnostics!.Diagnostics.Select(i => i.RuleId));
        scan.DiagnosticReport.Runs.Count.Should().Be(scan.Diagnostics.Runs.Count(r => r.ReasonCode != "RULE_OUTSIDE_SCOPE"));
    }

    [Fact]
    public void MultipleDeadlocks_ExportOnlySelectedEventAndRejectAnotherEventsAnalysis()
    {
        var first = SafeXmlHelper.ParseSafe(Utilities.EmbeddedResourceHelper.GetResourceContent("deadlock_bookmark_lookup.xdl")).Root!;
        var second = new XElement(first);
        first.Descendants().First(e => e.Name.LocalName == "inputbuf").Value = "SELECT 'FIRST_EVENT'";
        second.Descendants().First(e => e.Name.LocalName == "inputbuf").Value = "SELECT 'SECOND_EVENT'";
        var input = new InputRecognitionService().Parse(new XDocument(new XElement("deadlock-list", first, second)).ToString());
        input.Deadlocks.Should().HaveCount(2);
        var a = new DeadlockAnalysisService().Analyze(input.Deadlocks[0].Document);
        var b = new DeadlockAnalysisService().Analyze(input.Deadlocks[1].Document);
        var report = DiagnosticReportFactory.Deadlock(input.Deadlocks[1], b, input.Envelope);
        report.SourceXml.Should().Contain("SECOND_EVENT").And.NotContain("FIRST_EVENT");
        report.Scope.Should().Be("Event" + input.Deadlocks[1].Index);
        Action wrong = () => DiagnosticReportFactory.Deadlock(input.Deadlocks[1], a, input.Envelope);
        wrong.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("exe", "report.exe")]
    [InlineData("html", "report.sql")]
    public void UnsupportedFormatOrExtension_NeverInvokesWriter(string format, string name)
    {
        bool invoked = false;
        var service = new DiagnosticReportExportService(new Writer((_, _, _, _) => invoked = true));
        service.Export(Report(), format, Path.Combine(_directory, name)).Succeeded.Should().BeFalse();
        invoked.Should().BeFalse(); Directory.GetFiles(_directory).Should().BeEmpty();
    }

    internal sealed class Writer(Action<DiagnosticReport, string, string, CancellationToken> write) : IDiagnosticReportFileWriter
    {
        public void Write(DiagnosticReport report, string format, string path, CancellationToken token) => write(report, format, path, token);
    }
    public void Dispose()
    {
        if (!Path.GetFullPath(_directory).StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP22-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(_directory, true);
    }
}
