using System.IO;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanComparisonSourceHashTests
{
    private static readonly XNamespace SessionNs = "http://schemas.sqlxmlanalyzer.com/session";
    [Fact]
    public void SessionRoundtrip_PreservesOriginalByteHashInComparisonEvidence()
    {
        var source = PlanComparisonMultiStatementTests.RuntimeSnapshot(100).Document;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToString())));
        var envelope = PlanIdentityAdapter.GetDocument(source)!.Envelope with { SourceHash = hash };
        PlanIdentityAdapter.SetEnvelope(source, envelope);
        var service = new TuningSessionService();
        var snapshot = service.CaptureSnapshot(source, "source.sqlplan", 1);
        string path = Path.Combine(Path.GetTempPath(), "imp17-hash-" + Guid.NewGuid().ToString("N") + ".pesession");
        try
        {
            service.Save(path, [snapshot], snapshot, snapshot);
            var restored = service.Load(path).PlanA!;
            var capture = PlanComparisonMultiStatementTests.Compare(restored, restored).Statements.Single().QueryA!.Capture;
            capture.SourceHash.Should().Be(hash);
            capture.SourceHashKind.Should().Contain("原始文件字节");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("cost")]
    [InlineData("hash")]
    public void ModifiedSessionContentOrMalformedHash_IsRejected(string modification)
    {
        WithSavedSession((service, path, _) =>
        {
            var saved = SafeXmlHelper.LoadSafe(path);
            if (modification == "cost") saved.Descendants(PlanComparisonMultiStatementTests.Ns + "RelOp").First().SetAttributeValue("EstimatedTotalSubtreeCost", "999");
            else saved.Descendants(SessionNs + "Snapshot").First().SetAttributeValue("OriginalSourceHash", "not-a-hash");
            saved.Save(path, SaveOptions.DisableFormatting);
            Action load = () => service.Load(path);
            load.Should().Throw<InvalidDataException>().WithMessage("SESSION_*HASH*");
        });
    }

    [Fact]
    public void SourceEdit_DropsTheOldOriginalHashFromComparisonAndResavedSession()
    {
        WithSavedSession((service, path, _) =>
        {
            var snapshot = service.Load(path).PlanA!;
            snapshot.Document.Root!.SetAttributeValue("Build", "17.0.1.1");
            var capture = PlanComparisonMultiStatementTests.Compare(snapshot, snapshot).Statements.Single().QueryA!.Capture;
            capture.SourceHashKind.Should().Be("XML 表示");
            capture.SourceHash.Should().Be(snapshot.XmlContentHash);
            service.Save(path, [snapshot], snapshot, snapshot);
            var loaded = service.Load(path).PlanA!;
            var restored = PlanComparisonMultiStatementTests.Compare(loaded, loaded).Statements.Single().QueryA!.Capture;
            restored.SourceHashKind.Should().Be("XML 表示");
            restored.RecordedOriginalSourceHash.Should().NotBeNull();
            restored.Summary.Should().Contain("关联未核验");
        });
    }

    [Fact]
    public void LegacyIndentedSession_RestoresRecordedHashWithoutClaimingRawBytesWereRevalidated()
    {
        WithSavedSession((service, path, original) =>
        {
            var saved = SafeXmlHelper.LoadSafe(path);
            saved.Root!.SetAttributeValue("Version", "2.0");
            saved.Descendants(SessionNs + "Snapshot").First().Attribute("XmlContentHashFormat")!.Remove();
            saved.Save(path);
            var loaded = service.Load(path).PlanA!;
            var capture = PlanComparisonMultiStatementTests.Compare(loaded, loaded).Statements.Single().QueryA!.Capture;
            capture.SourceHash.Should().Be(original.OriginalSourceHash);
            capture.SourceHashKind.Should().Contain("会话记录").And.Contain("未重新读取");
            capture.XmlContentHash.Should().Be(loaded.XmlContentHash);
        });
    }

    [Fact]
    public void LegacySessionWithoutXmlHash_DoesNotPromoteUnboundOriginalHash()
    {
        WithSavedSession((service, path, _) =>
        {
            var saved = SafeXmlHelper.LoadSafe(path);
            var element = saved.Descendants(SessionNs + "Snapshot").First();
            element.Attribute("XmlContentHash")!.Remove(); element.Attribute("XmlContentHashFormat")!.Remove();
            saved.Save(path, SaveOptions.DisableFormatting);
            var loaded = service.Load(path).PlanA!;
            var capture = PlanComparisonMultiStatementTests.Compare(loaded, loaded).Statements.Single().QueryA!.Capture;
            capture.SourceHashKind.Should().Be("XML 表示");
            capture.SourceHash.Should().Be(loaded.XmlContentHash);
        });
    }

    private static void WithSavedSession(Action<TuningSessionService, string, PlanSnapshot> test)
    {
        var source = PlanComparisonMultiStatementTests.RuntimeSnapshot(100).Document;
        var envelope = PlanIdentityAdapter.GetDocument(source)!.Envelope with
            { SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToString()))) };
        PlanIdentityAdapter.SetEnvelope(source, envelope);
        var service = new TuningSessionService(); var snapshot = service.CaptureSnapshot(source, "source.sqlplan", 1);
        string path = Path.Combine(Path.GetTempPath(), "imp17-source-" + Guid.NewGuid().ToString("N") + ".pesession");
        try { service.Save(path, [snapshot], snapshot, snapshot); test(service, path, snapshot); }
        finally { File.Delete(path); }
    }
}
