using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Privacy;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class DiagnosticPackageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP27-" + Guid.NewGuid().ToString("N"));
    public DiagnosticPackageTests() => Directory.CreateDirectory(_directory);
    private string PathFor(string name) => Path.Combine(_directory, name);
    internal static DiagnosticPackage Prepare(DiagnosticReport? report = null, DiagnosticPackageOptions? options = null)
    {
        var result = new DiagnosticPackageBuilder().Prepare(report, RuleConfigurationDocument.Defaults, null, options ?? new());
        result.Success.Should().BeTrue(result.Message);
        return result.Package!;
    }
    private static JsonDocument Json(DiagnosticPackage package, string name) => JsonDocument.Parse(package.Entries.Single(e => e.Name == name).Preview);

    [Fact]
    public void Configuration_WithoutReport_UsesExecutableRuleVersions()
    {
        var engine = new RuleEngine(configuration: RuleConfigurationDocument.Defaults);
        engine.RegisterDefaultRules();
        var package = Prepare(options: new(IncludeReport: false));
        using var configuration = Json(package, "configuration.json");
        var versions = configuration.RootElement.GetProperty("RuleVersions").EnumerateArray()
            .ToDictionary(r => r.GetProperty("RuleId").GetString()!, r => r.GetProperty("Version").GetString()!);

        versions.Should().BeEquivalentTo(engine.RegisteredRules.ToDictionary(r => r.RuleId, r => r.Metadata.Version));
        versions["RULE_004_ESTIMATE_MISMATCH"].Should().Be("2.0.0");
        versions["RULE_006_RESIDUAL_PREDICATE"].Should().Be("2.0.0");
        versions["RULE_030_CARDINALITY_ERROR"].Should().Be("2.0.0");
        versions["RULE_034_RESIDUAL_PRED_OP"].Should().Be("2.0.0");
        versions["RULE_020_MISSING_INDEX"].Should().Be("2.1.1");
        versions["RULE_035_SARGABLE_INDEX_RECOMMENDATION"].Should().Be("2.1.1");
        configuration.RootElement.GetProperty("ReportRuleVersions").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configuration_CapturedRuleVersionRemainsSeparateFromInstalledVersion(bool disabled)
    {
        const string ruleId = "RULE_004_ESTIMATE_MISMATCH";
        var active = RuleConfigurationDocument.Parse($$"""{"Rules":[{"RuleId":"{{ruleId}}","Enabled":{{(!disabled).ToString().ToLowerInvariant()}},"SeverityOverride":"Critical"}]}""");
        var engine = new RuleEngine(configuration: active);
        engine.RegisterRule(new Rules.DiagnosticProtocolTests.NativeRule(ruleId, _ => RuleEvaluation.NoHit()));
        var xml = PlanRedactionServiceTests.Fixture();
        var diagnostics = engine.AnalyzePlanDetailed(xml, xml.Root!.Name.Namespace);
        diagnostics.Runs.Should().ContainSingle().Which.Status.Should().Be(disabled ? RuleRunStatus.Skipped : RuleRunStatus.NoHit);
        var report = DiagnosticReportFactory.Plan(xml, PlanIdentityAdapter.GetDocument(xml)!, diagnostics);
        var result = new DiagnosticPackageBuilder().Prepare(report, active, null, new(IncludeReport: false));
        result.Success.Should().BeTrue(result.Message);
        using var configuration = Json(result.Package!, "configuration.json");
        var installed = configuration.RootElement.GetProperty("RuleVersions").EnumerateArray()
            .Single(r => r.GetProperty("RuleId").GetString() == ruleId);
        installed.GetProperty("Version").GetString().Should().Be("2.0.0");
        installed.GetProperty("DefaultSeverity").GetString().Should().Be("Warning");
        var captured = configuration.RootElement.GetProperty("ReportRuleVersions").EnumerateArray().Single();
        captured.GetProperty("RuleVersion").GetString().Should().Be("2.1.0");
    }

    [Fact]
    public void DefaultPackage_ExcludesSensitiveMarkersAcrossEveryPayloadAndManifest()
    {
        string marker = PlanRedactionServiceTests.Marker;
        var config = RuleConfigurationDocument.Parse($$"""{"Rules":[{"RuleId":"{{marker}}","Payload":"{{marker}}"}],"Extension":"{{marker}}"}""");
        var result = new DiagnosticPackageBuilder().Prepare(DiagnosticReportTests.Report(), config,
            new(1, 1, AnalysisOperationState.Failed, marker, []), new());
        result.Success.Should().BeTrue(result.Message);
        var package = result.Package!;
        package.ContainsSensitiveContent.Should().BeFalse();
        package.Entries.Should().NotContain(e => e.Name == "selection.xml" || e.Name == "application.log" || e.Name == "process.dmp");
        package.Entries.Should().Contain(e => e.Name == "report.json");
        foreach (var entry in package.Entries) entry.Preview.Should().NotContain(marker).And.NotContain(_directory);
        using var gaps = Json(package, "missing-evidence.json");
        gaps.RootElement.GetProperty("Missing").EnumerateArray().Select(x => x.GetProperty("Code").GetString())
            .Should().Contain("CONFIGURATION_EXTENSIONS_OMITTED");
    }

    [Fact]
    public void Configuration_UsesReportSnapshotAndDistinguishesChangedActiveSession()
    {
        var report = DiagnosticReportTests.Report();
        string id = RuleMetadataCatalog.RegisteredRuleIds.First();
        var changed = RuleConfigurationDocument.Parse($$"""{"Rules":[{"RuleId":"{{id}}","Enabled":false,"SeverityOverride":"Critical"}]}""");
        var result = new DiagnosticPackageBuilder().Prepare(report, changed, null, new());
        result.Success.Should().BeTrue(result.Message);
        using var config = Json(result.Package!, "configuration.json");
        var analysis = config.RootElement.GetProperty("Analysis");
        analysis.GetProperty("Available").GetBoolean().Should().BeTrue();
        analysis.GetProperty("Rules").GetArrayLength().Should().Be(RuleMetadataCatalog.RegisteredRuleIds.Count);
        analysis.GetProperty("Fingerprint").GetString().Should().NotBe(changed.Fingerprint);
        config.RootElement.GetProperty("ActiveSession").GetProperty("Fingerprint").GetString().Should().Be(changed.Fingerprint);
        using var context = Json(result.Package!, "context.json");
        context.RootElement.GetProperty("ConfigurationChanged").GetBoolean().Should().BeTrue();
        context.RootElement.GetProperty("RuleProtocolVersion").GetString().Should().Be(PlanAnalysisContext.CurrentProtocolVersion);
    }

    [Fact]
    public void WithoutReport_RecordsUnavailableEvidenceAndNeverInventsAnalysisConfiguration()
    {
        var result = new DiagnosticPackageBuilder().Prepare(null, null, null, new());
        result.Success.Should().BeTrue(result.Message);
        result.Package!.Entries.Should().HaveCount(6);
        using var configuration = Json(result.Package, "configuration.json");
        configuration.RootElement.GetProperty("Analysis").GetProperty("Available").GetBoolean().Should().BeFalse();
        configuration.RootElement.GetProperty("ActiveSession").GetProperty("Available").GetBoolean().Should().BeFalse();
        using var gaps = Json(result.Package, "missing-evidence.json");
        gaps.RootElement.GetProperty("Missing").EnumerateArray().Should().Contain(x => x.GetProperty("Code").GetString() == "ANALYSIS_SNAPSHOT_UNAVAILABLE");
    }

    [Fact]
    public void ExplicitSelection_IncludesRawXmlAndLogWithFixedNamesAndVisibleSensitiveStatus()
    {
        string path = PathFor("SECRET-source-name.log");
        File.WriteAllText(path, "SECRET_LOG_CONTENT", new UTF8Encoding(false));
        var report = DiagnosticReportTests.Report();
        var package = Prepare(report, new(IncludeRawSelection: true, LogPath: path));
        package.ContainsSensitiveContent.Should().BeTrue();
        package.Entries.Single(e => e.Name == "selection.xml").Preview.Should().Be(report.SourceXml);
        package.Entries.Single(e => e.Name == "application.log").Preview.Should().Be("SECRET_LOG_CONTENT");
        package.Entries.Where(e => e.Name is "selection.xml" or "application.log").Should().OnlyContain(e => e.Privacy == "RawSensitive");
        package.Entries.Single(e => e.Name == "manifest.json").Preview.Should().NotContain("SECRET-source-name");
    }

    [Fact]
    public void Save_UsesReviewedBytesEvenWhenSelectedLogChangesOrDisappears()
    {
        string log = PathFor("selected.log"); File.WriteAllText(log, "REVIEWED_LOG");
        var package = Prepare(options: new(LogPath: log));
        File.WriteAllText(log, "UNREVIEWED_CONTENT"); File.Delete(log);
        string path = PathFor("package.zip");
        new DiagnosticPackageExporter().Save(package, path).Success.Should().BeTrue();
        DiagnosticPackageValidator.Validate(path);
        using var zip = ZipFile.OpenRead(path);
        zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo(package.Entries.Select(e => e.Name));
        foreach (var entry in package.Entries)
        {
            using var input = zip.GetEntry(entry.Name)!.Open();
            Convert.ToHexString(SHA256.HashData(input)).Should().Be(entry.Sha256);
        }
        using var reader = new StreamReader(zip.GetEntry("application.log")!.Open());
        reader.ReadToEnd().Should().Be("REVIEWED_LOG");
    }

    [Fact]
    public void InvalidUtf8Log_ReportsEncodingErrorWithoutDumpOrPackage()
    {
        string path = PathFor("invalid-encoding.log");
        File.WriteAllBytes(path, [0xFF, 0xFE, 0x41]);
        var reporter = new CountingReporter();
        var result = new DiagnosticPackageBuilder(reporter).Prepare(null, null, null, new(LogPath: path));

        result.Success.Should().BeFalse();
        result.Package.Should().BeNull();
        result.Message.Should().Contain("UTF-8").And.NotContain("路径无效");
        reporter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("extension")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("unicode")]
    [InlineData("missing")]
    [InlineData("dump")]
    [InlineData("dump-header")]
    public void InvalidAttachment_ReturnsNoPackageWithoutDump(string kind)
    {
        string path = PathFor(kind == "extension" ? "input.sqlplan" : kind.StartsWith("dump") ? "input.dmp" : "input.log");
        File.WriteAllText(path, kind == "empty" ? "" : "text");
        if (kind == "missing") File.Delete(path);
        if (kind == "unicode") File.WriteAllBytes(path, [0xff, 0xff]);
        if (kind == "dump-header") { var header = new byte[32]; "MDMP"u8.CopyTo(header); File.WriteAllBytes(path, header); }
        if (kind == "oversize") { using var stream = File.OpenWrite(path); stream.SetLength(DiagnosticPackage.MaxLogBytes + 1); }
        var reporter = new CountingReporter();
        var result = new DiagnosticPackageBuilder(reporter).Prepare(null, null, null,
            kind.StartsWith("dump") ? new(DumpPath: path) : new(LogPath: path));
        result.Success.Should().BeFalse(); reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void UnknownXml_BlocksSelectedReportButAllowsMetadataOnlyPackage()
    {
        var xml = PlanRedactionServiceTests.Fixture(); xml.Root!.SetAttributeValue("UncoveredSecret", "secret");
        var report = DiagnosticReportTests.Report(xml);
        var builder = new DiagnosticPackageBuilder();
        builder.Prepare(report, null, null, new()).Package.Should().BeNull();
        var package = Prepare(report, new(IncludeReport: false));
        package.Entries.Should().NotContain(e => e.Name == "report.json");
        package.Entries.Should().OnlyContain(e => !e.Preview.Contains("UncoveredSecret"));
    }

    [Theory]
    [InlineData("existing")]
    [InlineData("directory")]
    [InlineData("extension")]
    [InlineData("missing-parent")]
    [InlineData("cancel")]
    public void Save_ExpectedFailurePreservesExistingFilesAndLeavesNoTemporaryData(string kind)
    {
        string path = PathFor(kind == "extension" ? "output.sqlplan" : "output.zip");
        if (kind == "existing") File.WriteAllText(path, "ORIGINAL");
        if (kind == "directory") Directory.CreateDirectory(path);
        if (kind == "missing-parent") path = Path.Combine(_directory, "absent", "output.zip");
        var reporter = new CountingReporter();
        var result = new DiagnosticPackageExporter(unexpectedErrors: reporter).Save(Prepare(), path, new(kind == "cancel"));
        result.Success.Should().BeFalse(); reporter.Calls.Should().Be(0);
        if (kind == "existing") File.ReadAllText(path).Should().Be("ORIGINAL");
        else File.Exists(path).Should().BeFalse();
        Directory.GetDirectories(_directory, ".tmp.*").Should().BeEmpty();
    }

    [Fact]
    public void Save_CancelAfterArchiveWriteDoesNotPublish()
    {
        using var cancellation = new CancellationTokenSource();
        var writer = new ActionWriter((package, path, token) =>
        { new DiagnosticPackageZipWriter().Write(package, path, token); cancellation.Cancel(); });
        string destination = PathFor("cancelled.zip");
        new DiagnosticPackageExporter(writer).Save(Prepare(), destination, cancellation.Token).Success.Should().BeFalse();
        File.Exists(destination).Should().BeFalse(); Directory.GetDirectories(_directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("substitute")]
    public void Save_InvalidOrSubstitutedOutputCannotReplaceReviewedPackage(string kind)
    {
        var alternate = Prepare(options: new(IncludeReport: false));
        var writer = new ActionWriter((package, path, token) =>
        {
            if (kind == "corrupt") File.WriteAllText(path, "not zip");
            else new DiagnosticPackageZipWriter().Write(alternate, path, token);
        });
        string path = PathFor("bad.zip");
        var reporter = new CountingReporter();
        new DiagnosticPackageExporter(writer, reporter).Save(Prepare(DiagnosticReportTests.Report()), path).Success.Should().BeFalse();
        File.Exists(path).Should().BeFalse(); reporter.Calls.Should().Be(0);
        Directory.GetDirectories(_directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("traversal")]
    [InlineData("version")]
    [InlineData("manifest-size")]
    public void Validate_RejectsCorruptionOrInvalidManifest(string kind)
    {
        string path = PathFor("tampered.zip");
        new DiagnosticPackageExporter().Save(Prepare(), path).Success.Should().BeTrue();
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            void Replace(string name, string text)
            {
                archive.GetEntry(name)?.Delete();
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text);
            }
            if (kind == "payload") Replace("context.json", "{}");
            else if (kind is "extra" or "duplicate" or "traversal") archive.CreateEntry(kind == "extra" ? "secret.sql" : kind == "duplicate" ? "context.json" : "../context.json");
            else
            {
                JsonNode node;
                using (var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open())) node = JsonNode.Parse(reader.ReadToEnd())!;
                if (kind == "version") node["SchemaVersion"] = "IMP27-future";
                else node["Files"]![0]!["Bytes"] = 999;
                Replace("manifest.json", node.ToJsonString());
                var sums = new StringBuilder();
                foreach (var entry in archive.Entries.Where(e => e.FullName != "SHA256SUMS"))
                { using var stream = entry.Open(); sums.Append(Convert.ToHexString(SHA256.HashData(stream))).Append("  ").Append(entry.FullName).Append('\n'); }
                Replace("SHA256SUMS", sums.ToString());
            }
        }
        Action validate = () => DiagnosticPackageValidator.Validate(path);
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Review_ChangedOptionsInvalidatePreviewAndCancelledPrepareCannotEnableSave()
    {
        var model = new DiagnosticPackageViewModel(DiagnosticReportTests.Report(), RuleConfigurationDocument.Defaults);
        model.IncludeRawSelection.Should().BeFalse(); model.LogPath.Should().BeNull(); model.DumpPath.Should().BeNull();
        model.CanSave.Should().BeFalse();
        await model.PrepareAsync(); model.CanSave.Should().BeTrue();
        model.SelectedEntry!.Name.Should().Be("manifest.json");
        model.IncludeReport = false; model.CanSave.Should().BeFalse(); model.Entries.Should().BeEmpty();
        await model.PrepareAsync(new(true)); model.CanSave.Should().BeFalse(); model.IsBusy.Should().BeFalse();
        await model.PrepareAsync(); model.CanSave.Should().BeTrue(); model.Entries.Should().NotContain(e => e.Name == "report.json");
    }

    [Fact]
    public async Task Review_BusySaveFreezesOptionsAndPreventsDuplicateWrites()
    {
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int calls = 0;
        var writer = new ActionWriter((package, path, token) =>
        {
            Interlocked.Increment(ref calls); started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            new DiagnosticPackageZipWriter().Write(package, path, token);
        });
        var model = new DiagnosticPackageViewModel(null, RuleConfigurationDocument.Defaults, writer: writer);
        await model.PrepareAsync();
        var pending = model.SaveAsync(PathFor("first.zip"));
        try
        {
            started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            model.CanEdit.Should().BeFalse(); model.CanSave.Should().BeFalse();
            model.IncludeRawSelection = true; model.LogPath = "different.log";
            model.IncludeRawSelection.Should().BeFalse(); model.LogPath.Should().BeNull();
            await model.SaveAsync(PathFor("second.zip")); calls.Should().Be(1);
        }
        finally { release.Set(); await pending; }
        model.CanSave.Should().BeTrue(); File.Exists(PathFor("second.zip")).Should().BeFalse();
    }

    [Fact]
    public void ErrorAndMissingEvidenceSummaries_PreserveFailedAndSkippedLocationsWithoutExceptionText()
    {
        var engine = new RuleEngine(unexpectedErrors: new CountingReporter());
        engine.RegisterRule(new Rules.DiagnosticProtocolTests.NativeRule("RULE_030_CARDINALITY_ERROR", _ => throw new InvalidOperationException("PRIVATE_EXCEPTION_MARKER")));
        engine.RegisterRule(new Rules.DiagnosticProtocolTests.NativeRule("RULE_034_RESIDUAL_PRED_OP", _ => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "PRIVATE_REASON_MARKER")));
        var xml = PlanRedactionServiceTests.Fixture();
        var diagnostics = engine.AnalyzePlanDetailed(xml, xml.Root!.Name.Namespace);
        var report = DiagnosticReportFactory.Plan(xml, PlanIdentityAdapter.GetDocument(xml)!, diagnostics);
        var package = Prepare(report);
        using var errors = Json(package, "errors.json");
        errors.RootElement.GetProperty("FailedRuleCount").GetInt32().Should().Be(1);
        var failure = errors.RootElement.GetProperty("FailedRules")[0];
        failure.GetProperty("RuleId").GetString().Should().Be("RULE_030_CARDINALITY_ERROR");
        failure.GetProperty("Location").GetString().Should().Be("B1/S1/Q1/O1");
        using var missing = Json(package, "missing-evidence.json");
        missing.RootElement.GetProperty("RuleGaps").GetArrayLength().Should().Be(2);
        foreach (var entry in package.Entries) entry.Preview.Should().NotContain("PRIVATE_EXCEPTION_MARKER").And.NotContain("PRIVATE_REASON_MARKER");
    }

    [Fact]
    public void EstimateOnlyPlan_RecordsMissingRuntimeEvidence()
    {
        var xml = PlanRedactionServiceTests.Fixture();
        foreach (var node in xml.Descendants().Where(e => e.Name.LocalName == "RunTimeInformation").ToArray()) node.Remove();
        var package = Prepare(DiagnosticReportTests.Report(xml));
        using var gaps = Json(package, "missing-evidence.json");
        gaps.RootElement.GetProperty("Missing").EnumerateArray().Select(x => x.GetProperty("Code").GetString())
            .Should().Contain("RUNTIME_COUNTERS_UNAVAILABLE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingEvidence_ParallelPlanPreservesIncompleteAndAmbiguousMetrics(bool includeReport)
    {
        var xml = SafeXmlHelper.ParseSafe("""
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.539" Build="16.0.1">
            <BatchSequence><Batch><Statements><StmtSimple><QueryPlan>
            <RelOp NodeId="0" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="1" EstimateRebinds="0">
            <RunTimeInformation>
            <RunTimeCountersPerThread Thread="1" ActualRows="10" ActualExecutions="1" ActualCPUms="-1" />
            <RunTimeCountersPerThread Thread="2" ActualRows="20" ActualExecutions="2" ActualCPUms="2" />
            </RunTimeInformation></RelOp>
            </QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>
            """);
        var package = Prepare(DiagnosticReportTests.Report(xml), new(IncludeReport: includeReport));
        using var missing = Json(package, "missing-evidence.json");
        var gaps = missing.RootElement.GetProperty("MetricGaps").EnumerateArray()
            .ToDictionary(g => g.GetProperty("Name").GetString()!, g => g.GetProperty("State").GetString()!);

        gaps.Should().Contain("B1/S1/Q1/O1/EstimatedExecutions/State", "Incomplete");
        gaps.Should().Contain("B1/S1/Q1/O1/LogicalExecutions/State", "Ambiguous");
        gaps.Should().Contain("B1/S1/Q1/O1/RowsPerExecution/State", "Ambiguous");
        gaps.Should().Contain("B1/S1/Q1/O1/RowsRead/State", "Missing");
        gaps.Should().Contain("B1/S1/Q1/O1/CpuTime/State", "Incomplete");
        gaps.Should().Contain("B1/S1/Q1/O1/Threads[1]/CpuTime/State", "Invalid");
        gaps.Should().NotContainKey("B1/S1/Q1/O1/OutputRows/State");
        gaps.Values.Should().OnlyContain(state => state != "Available");
    }

    [Fact]
    public void MissingEvidence_OnlyCanonicalUnclassifiedMetricStatesEnterSummary()
    {
        var report = new DiagnosticReport("ExecutionPlan", "Document", [],
        [
            new("MissingMetric/State", "Missing"), new("InvalidMetric/State", "Invalid"),
            new("IncompleteMetric/State", "Incomplete"), new("AmbiguousMetric/State", "Ambiguous"),
            new("AvailableMetric/State", "Available"), new("UnknownMetric/State", "Unsupported"),
            new("NumericMetric/State", "1"), new("UndefinedMetric/State", "999"),
            new("CombinedMetric/State", "Missing, Invalid"), new("WhitespaceMetric/State", " Missing "),
            new("CaseMetric/State", "missing"), new("NotAState/StateSuffix", "Missing"),
            new("PRIVATE_FIELD/State", "Missing", "FactText")
        ], [], [], [], [], "");
        var package = Prepare(report, new(IncludeReport: false));
        using var missing = Json(package, "missing-evidence.json");
        var gaps = missing.RootElement.GetProperty("MetricGaps").EnumerateArray()
            .ToDictionary(g => g.GetProperty("Name").GetString()!, g => g.GetProperty("State").GetString()!);

        gaps.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["MissingMetric/State"] = "Missing", ["InvalidMetric/State"] = "Invalid",
            ["IncompleteMetric/State"] = "Incomplete", ["AmbiguousMetric/State"] = "Ambiguous"
        });
        foreach (var entry in package.Entries) entry.Preview.Should().NotContain("PRIVATE_FIELD");
    }

    [Fact]
    public void PreviouslyRedactedReport_DoesNotMisrepresentCurrentConfigAsAnalysisConfig()
    {
        var redacted = new ReportRedactionService().Preview(DiagnosticReportTests.Report()).Report!;
        var package = Prepare(redacted, new(IncludeReport: false));
        using var config = Json(package, "configuration.json");
        config.RootElement.GetProperty("Analysis").GetProperty("Available").GetBoolean().Should().BeFalse();
        config.RootElement.GetProperty("ActiveSession").GetProperty("Available").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void MissingRawSelection_FailsInsteadOfSilentlyOmittingChosenContent()
    {
        new DiagnosticPackageBuilder().Prepare(null, null, null, new(IncludeRawSelection: true)).Success.Should().BeFalse();
    }

    [Fact]
    public void Save_WhenDestinationAppearsDuringWrite_PreservesIt()
    {
        string destination = PathFor("concurrent.zip");
        var writer = new ActionWriter((package, path, token) =>
        { new DiagnosticPackageZipWriter().Write(package, path, token); File.WriteAllText(destination, "CONCURRENT_FILE"); });
        new DiagnosticPackageExporter(writer).Save(Prepare(), destination).Success.Should().BeFalse();
        File.ReadAllText(destination).Should().Be("CONCURRENT_FILE");
        Directory.GetDirectories(_directory).Should().BeEmpty();
    }

    [Fact]
    public void PartialIoFailure_RemovesUnpublishedSensitiveArchive()
    {
        var writer = new ActionWriter((_, path, _) => { File.WriteAllText(path, "PARTIAL_SENSITIVE_CONTENT"); throw new IOException("synthetic storage failure"); });
        string destination = PathFor("partial.zip");
        new DiagnosticPackageExporter(writer).Save(Prepare(), destination).Success.Should().BeFalse();
        Directory.GetFiles(_directory).Should().BeEmpty(); Directory.GetDirectories(_directory).Should().BeEmpty();
    }

    internal sealed class ActionWriter(Action<DiagnosticPackage, string, CancellationToken> action) : IDiagnosticPackageWriter
    { public void Write(DiagnosticPackage package, string path, CancellationToken token) => action(package, path, token); }
    private sealed class CountingReporter : IUnexpectedErrorReporter
    { public int Calls; public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; return new(null, null, "test"); } }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
