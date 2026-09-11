using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests;

/// <summary>IMP-29: exercise public boundaries in the order used by a local review task.</summary>
public sealed class AcceptanceUserScenarioTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP29-" + Guid.NewGuid().ToString("N"));
    public AcceptanceUserScenarioTests() => Directory.CreateDirectory(_directory);
    private string PathFor(string name) => Path.Combine(_directory, name);

    [Theory]
    [InlineData(0, "T", "U")]
    [InlineData(1, "U", "T")]
    public async Task Plan_OpenSelectEvidenceSaveReopenCompareAndPackage_PreservesScope(int index, string selected, string other)
    {
        string path = PathFor("input.sqlplan");
        File.WriteAllText(path, InputRecognitionTests.Fixture("imp14_cardinality_residual.sqlplan"));
        byte[] original = File.ReadAllBytes(path);
        var input = await new InputRecognitionService().ReadFileAsync(path);
        input.IsSuccess.Should().BeTrue();
        var document = input.Document!;
        var diagnostics = PlanDiagnosticAnalyzer.AnalyzeDetailed(document, document.Root!.Name.Namespace);
        var workspace = new PlanWorkspaceViewModel();
        workspace.Open(document, diagnostics, [], input);
        workspace.SelectedStatement = workspace.Statements[index];
        workspace.SelectedIssue = workspace.Issues.First(i => i.Diagnostic != null && i.Evidence.Count > 0);
        workspace.SelectedEvidence = workspace.Evidence.First();
        workspace.SourceTarget!.Sql.Should().Contain("FROM " + selected).And.NotContain("FROM " + other);
        workspace.SourceTarget.Location.Statement.Should().Be(workspace.SelectedStatement.Statement.Key);

        var sessions = new TuningSessionService();
        var snapshot = sessions.CaptureSnapshot(document, path, 1);
        string sessionPath = PathFor("review.pesession");
        sessions.Save(sessionPath, [snapshot], snapshot, snapshot);
        var restored = sessions.Load(sessionPath);
        var comparison = new PlanComparisonController().BuildComparison(restored.PlanA!, restored.PlanB!, document.Root.Name.Namespace);
        comparison.Statements.Should().HaveCount(2);
        comparison.Statements.Should().OnlyContain(pair => pair.A != null && pair.B != null);
        // This fixture did not capture cost/context; round trips must not turn missing metrics into zeros.
        comparison.Statements.SelectMany(pair => pair.RootsB).Should().OnlyContain(node => node.CostDelta == null);

        var report = workspace.CreateReport();
        report.Scope.Should().Be($"B1/S{index + 1}/Q1");
        report.SourceXml.Should().Contain("FROM " + selected).And.NotContain("FROM " + other);
        var preview = new ReportRedactionService().Preview(report);
        preview.CanExport.Should().BeTrue(preview.Summary);
        preview.Report!.Facts.Single(f => f.Name == report.Scope + "/O1/OutputRows/Value").Value.Should().Be("10000");
        preview.Report.Facts.Single(f => f.Name == report.Scope + "/O1/RowsRead/Value").Value.Should().Be("100000");
        VerifyExportsAndPackage(report);
        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Theory]
    [InlineData(0, "p1", "p3")]
    [InlineData(1, "p3", "p1")]
    public async Task Deadlock_OpenMultipleEventsAnalyzeAndPackage_UsesOnlySelectedEvent(int index, string victim, string otherVictim)
    {
        string path = PathFor("events.xdl");
        File.WriteAllText(path, InputRecognitionTests.Fixture("deadlock_multiple_events.xdl"));
        byte[] original = File.ReadAllBytes(path);
        var input = await new InputRecognitionService().ReadFileAsync(path);
        input.Deadlocks.Should().HaveCount(2);
        var selected = input.Deadlocks[index];
        var analysis = new DeadlockAnalysisService().Analyze(selected.Document);
        analysis.Graph.VictimProcessIds.Should().Contain(victim).And.NotContain(otherVictim);
        var report = DiagnosticReportFactory.Deadlock(selected, analysis, input.Envelope);
        report.Scope.Should().Be("Event" + selected.Index);
        report.Nodes.Should().HaveCount(2);
        report.SourceXml.Should().Contain(victim).And.NotContain(otherVictim);
        report.Metadata.Should().Contain(f => f.Name == "Nature" && f.Value.Contains("不是真实"));
        VerifyExportsAndPackage(report);
        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Package_CancelledOrExistingDestination_CanRetryWithoutLosingReviewedBytes(bool existing)
    {
        var report = DiagnosticReportTests.Report();
        var prepared = new DiagnosticPackageBuilder().Prepare(report, RuleConfigurationDocument.Defaults, null, new());
        prepared.Success.Should().BeTrue(prepared.Message);
        string destination = PathFor("review.zip");
        if (existing) File.WriteAllText(destination, "existing-owner");
        var exporter = new DiagnosticPackageExporter();
        exporter.Save(prepared.Package!, destination, new CancellationToken(!existing)).Success.Should().BeFalse();
        if (existing) File.ReadAllText(destination).Should().Be("existing-owner");
        else File.Exists(destination).Should().BeFalse();
        string retry = PathFor("retry.zip");
        exporter.Save(prepared.Package!, retry).Success.Should().BeTrue();
        VerifyZip(prepared.Package!, retry);
    }

    private void VerifyExportsAndPackage(DiagnosticReport report)
    {
        var redacted = new ReportRedactionService().Preview(report);
        redacted.CanExport.Should().BeTrue(redacted.Summary);
        foreach (string format in new[] { "json", "html" })
        {
            string destination = PathFor("report." + format);
            var result = new DiagnosticReportExportService().Export(redacted.Report!, format, destination);
            result.Succeeded.Should().BeTrue(result.Message);
            File.ReadAllText(destination).Should().Contain(report.Scope);
        }
        var prepared = new DiagnosticPackageBuilder().Prepare(report, RuleConfigurationDocument.Defaults, null, new());
        prepared.Success.Should().BeTrue(prepared.Message);
        prepared.Package!.ContainsSensitiveContent.Should().BeFalse();
        prepared.Package.Entries.Should().NotContain(e => e.Name == "selection.xml" || e.Name == "application.log" || e.Name == "process.dmp");
        using var packaged = JsonDocument.Parse(prepared.Package.Entries.Single(e => e.Name == "report.json").Preview);
        packaged.RootElement.GetProperty("Scope").GetString().Should().Be(report.Scope);
        string zip = PathFor("package.zip");
        new DiagnosticPackageExporter().Save(prepared.Package, zip).Success.Should().BeTrue();
        VerifyZip(prepared.Package, zip);
    }

    private static void VerifyZip(DiagnosticPackage package, string path)
    {
        DiagnosticPackageValidator.Validate(path);
        using var zip = ZipFile.OpenRead(path);
        zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo(package.Entries.Select(e => e.Name));
        foreach (var entry in package.Entries)
        {
            using var bytes = zip.GetEntry(entry.Name)!.Open();
            Convert.ToHexString(SHA256.HashData(bytes)).Should().Be(entry.Sha256);
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
