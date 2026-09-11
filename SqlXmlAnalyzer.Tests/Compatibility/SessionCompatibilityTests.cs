using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests.Compatibility;

public sealed class SessionCompatibilityTests : IDisposable
{
    private static readonly XNamespace Ns = "http://schemas.sqlxmlanalyzer.com/session";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP28-session-" + Guid.NewGuid().ToString("N"));
    public SessionCompatibilityTests() => Directory.CreateDirectory(_directory);
    private string PathFor(string name) => Path.Combine(_directory, name);
    private string Legacy()
    {
        string path = PathFor("legacy.pesession");
        File.WriteAllText(path, CompatibilityFixtures.Read("session-v2.0.pesession"));
        return path;
    }

    [Theory]
    [InlineData("99.0")]
    [InlineData("2.2")]
    [InlineData("1.0")]
    [InlineData("")]
    public void Load_UnknownVersionFailsWithoutChangingOriginal(string version)
    {
        string path = Legacy();
        var xml = SafeXmlHelper.LoadSafe(path);
        xml.Root!.SetAttributeValue("Version", version);
        xml.Save(path);
        byte[] original = File.ReadAllBytes(path);
        Action load = () => new TuningSessionService().Load(path);
        load.Should().Throw<InvalidDataException>().WithMessage("SESSION_VERSION_UNSUPPORTED*");
        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("attribute")]
    [InlineData("element")]
    [InlineData("duplicate-id")]
    [InlineData("missing-plan-reference")]
    [InlineData("plan-text")]
    [InlineData("pair-text")]
    public void Load_UnsupportedOrAmbiguousStructureCannotBecomeAnEmptyOrLossySession(string kind)
    {
        string path = Legacy();
        var xml = SafeXmlHelper.LoadSafe(path);
        if (kind == "root") xml.Root!.Name = Ns + "FutureSession";
        else if (kind == "attribute") xml.Root!.SetAttributeValue("FutureOption", "preserve-me");
        else if (kind == "element") xml.Root!.Add(new XElement(Ns + "FutureState", "preserve-me"));
        else if (kind == "duplicate-id") xml.Descendants(Ns + "Snapshot").Last().SetAttributeValue("Id", "historical-a");
        else if (kind == "plan-text") xml.Descendants(Ns + "PlanDoc").First().Add("unrecognized payload");
        else if (kind == "pair-text") xml.Root!.Add(new XElement(Ns + "ComparisonSelection", "unrecognized payload"));
        else xml.Root!.SetAttributeValue("PlanAId", "unresolved");
        xml.Save(path);
        byte[] original = File.ReadAllBytes(path);
        Action load = () => new TuningSessionService().Load(path);
        load.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Fact]
    public void Load_ActualPreUpgradeCurrentSessionPreservesSnapshotAndSelection()
    {
        string path = PathFor("before.pesession");
        File.WriteAllText(path, CompatibilityFixtures.Read("session-v2.1-before.pesession"));
        byte[] original = File.ReadAllBytes(path);
        var result = new TuningSessionService().Load(path);
        result.SourceVersion.Should().Be("2.1");
        result.RequiresMigration.Should().BeFalse();
        result.Snapshots.Should().ContainSingle();
        result.PlanA.Should().BeSameAs(result.PlanB);
        result.PlanA!.CaptureTime.Should().Be(new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc));
        result.PlanA.StatementText.Should().Be("SELECT Id FROM dbo.CompatibilityFixture");
        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Fact]
    public void Save_LegacyCopyCannotOverwriteTheOnlyOriginal()
    {
        string source = Legacy();
        byte[] original = File.ReadAllBytes(source);
        var service = new TuningSessionService();
        var loaded = service.Load(source);
        loaded.SourceVersion.Should().Be("2.0");
        loaded.RequiresMigration.Should().BeTrue();
        Action overwrite = () => service.Save(source, loaded.Snapshots, loaded.PlanA, loaded.PlanB);
        overwrite.Should().Throw<IOException>();
        File.ReadAllBytes(source).Should().Equal(original);

        string destination = PathFor("converted.pesession");
        service.Save(destination, loaded.Snapshots, loaded.PlanA, loaded.PlanB);
        var current = service.Load(destination);
        current.SourceVersion.Should().Be("2.1");
        current.RequiresMigration.Should().BeFalse();
        current.Snapshots.Should().HaveCount(2);
        current.PlanA!.Title.Should().Be("Legacy A");
        current.PlanB!.Title.Should().Be("Legacy B");
        current.PlanA.StatementText.Should().Be("SELECT 1");
        current.PlanB.TotalCost.Should().Be(0.5);
        File.ReadAllBytes(source).Should().Equal(original);
        Directory.GetFiles(_directory).Should().HaveCount(2);
    }

    [Fact]
    public void Load_UnversionedLegacySessionCanBeSavedAsCurrentCopy()
    {
        string source = Legacy();
        var xml = SafeXmlHelper.LoadSafe(source); xml.Root!.Attribute("Version")!.Remove(); xml.Save(source);
        var service = new TuningSessionService();
        var legacy = service.Load(source);
        legacy.SourceVersion.Should().Be("unversioned");
        byte[] original = File.ReadAllBytes(source);
        string destination = PathFor("current.pesession");
        service.Save(destination, legacy.Snapshots, legacy.PlanA, legacy.PlanB);
        service.Load(destination).RequiresMigration.Should().BeFalse();
        File.ReadAllBytes(source).Should().Equal(original);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("cancel")]
    [InlineData("corrupt")]
    [InlineData("concurrent")]
    public void Save_FailureRetainsOriginalAndRemovesUnpublishedTemporaryData(string scenario)
    {
        string source = Legacy();
        byte[] original = File.ReadAllBytes(source);
        var loaded = new TuningSessionService().Load(source);
        string destination = PathFor("output.pesession");
        using var cancellation = new CancellationTokenSource();
        var reporter = new CountingReporter();
        var writer = new ActionWriter((path, bytes, token) =>
        {
            if (scenario == "io") { File.WriteAllText(path, "partial-data"); throw new IOException("synthetic storage error"); }
            if (scenario == "corrupt") { File.WriteAllText(path, "substituted-data"); return; }
            new TuningSessionWriter().Write(path, bytes, token);
            if (scenario == "cancel") cancellation.Cancel();
            if (scenario == "concurrent") File.WriteAllText(destination, "concurrent-owner");
        });
        Action save = () => new TuningSessionService(writer, reporter)
            .Save(destination, loaded.Snapshots, loaded.PlanA, loaded.PlanB, null, cancellation.Token);
        if (scenario == "cancel") save.Should().Throw<OperationCanceledException>();
        else if (scenario == "corrupt") save.Should().Throw<InvalidDataException>();
        else save.Should().Throw<IOException>();
        if (scenario == "concurrent") File.ReadAllText(destination).Should().Be("concurrent-owner");
        else File.Exists(destination).Should().BeFalse();
        reporter.Calls.Should().Be(0);
        File.ReadAllBytes(source).Should().Equal(original);
        Directory.GetDirectories(_directory, ".tmp.session-*").Should().BeEmpty();
    }

    [Fact]
    public void Load_UnsupportedVersionDoesNotReplaceActiveHistory()
    {
        string source = Legacy();
        var existing = new TuningSessionService().Load(source);
        var model = new MainViewModel();
        foreach (var snapshot in existing.Snapshots) model.TuningHistory.Add(snapshot);
        model.SetComparisonPlans(existing.PlanA, existing.PlanB);
        var messages = new List<string>(); model.ShowMessageBox += messages.Add;
        var xml = SafeXmlHelper.LoadSafe(source); xml.Root!.SetAttributeValue("Version", "99"); xml.Save(source);
        model.LoadSession(source);
        model.TuningHistory.Should().Equal(existing.Snapshots);
        model.PlanA.Should().BeSameAs(existing.PlanA); model.PlanB.Should().BeSameAs(existing.PlanB);
        messages.Should().ContainSingle().Which.Should().Contain("SESSION_VERSION_UNSUPPORTED");
    }

    internal sealed class ActionWriter(Action<string, ReadOnlyMemory<byte>, CancellationToken> action) : ITuningSessionWriter
    { public void Write(string path, ReadOnlyMemory<byte> bytes, CancellationToken token) => action(path, bytes, token); }
    private sealed class CountingReporter : IUnexpectedErrorReporter
    { public int Calls; public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; return new(null, null, "test"); } }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
