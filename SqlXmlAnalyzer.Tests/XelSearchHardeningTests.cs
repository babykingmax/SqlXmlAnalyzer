using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Services;
using static SqlXmlAnalyzer.Tests.XelSearchServiceTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class XelSearchHardeningTests
{
    [Fact]
    public void DatabaseName_FromCurrentdbname_IsSearchableAndPartOfStructureIdentity()
    {
        InputRecognitionResult WithDatabase(string name) => new InputRecognitionService().Parse(
            Fixture().Document!.ToString().Replace("currentdb=\"7\"", $"currentdb=\"7\" currentdbname=\"{name}\"", StringComparison.Ordinal));
        var service = new XelSearchService();
        var catalog = service.Build([Source(input: WithDatabase("RuntimeDatabase")), Source("b.xel", WithDatabase("OtherDatabase"))]);
        service.Search(catalog, new(Database: "rUNTIMEdATABASE")).Events.Should().ContainSingle().Which.SourcePath.Should().Be("a.xel");
        service.Search(catalog, new(Database: "7")).Events.Should().HaveCount(2);
        service.Search(catalog, new()).Groups.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("event")]
    [InlineData("original")]
    [InlineData("fields")]
    [InlineData("actions")]
    [InlineData("bytes")]
    [InlineData("members")]
    public async Task ChangedSnapshot_CannotAcquireNewBaselineOnImportOrRebuild(string target)
    {
        var record = Record("xml_deadlock_report", Fixture().Deadlocks[0].Document.ToString(), 128);
        var bytes = new byte[32];
        using var stream = new MemoryStream(bytes);
        var parsed = await new XelReader(new EventSource([record])).ReadDocumentAsync(stream);
        var members = parsed.Deadlocks.ToList();
        var input = parsed with { Deadlocks = members, SourceSnapshot = bytes };
        var reader = new SequenceReader(input);
        var service = new XelSearchService(reader);
        var catalog = service.Build([Source(input: input)]);
        var entry = catalog.Events[0]; string preview = entry.XmlPreview;
        switch (target)
        {
            case "event": entry.Event.Document.Root!.SetAttributeValue("changed", true); break;
            case "original": entry.Event.OriginalElement!.SetAttributeValue("changed", true); break;
            case "fields": ((Dictionary<string, string>)record.Fields)["future"] = "changed"; break;
            case "actions": ((Dictionary<string, string>)record.Actions)["database_name"] = "changed"; break;
            case "bytes": bytes[0] = 1; break;
            default: members[0] = members[0] with { Index = 99 }; break;
        }
        FluentActions.Invoking(entry.ValidateSource).Should().Throw<InvalidDataException>().WithMessage("*已改变*");
        await FluentActions.Awaiting(() => service.LoadAsync(catalog.Sources, ["new.xel"], default))
            .Should().ThrowAsync<InvalidDataException>().WithMessage("*已改变*");
        FluentActions.Invoking(() => service.Build(catalog.Sources)).Should().Throw<InvalidDataException>();
        reader.Calls.Should().Be(0);
        entry.XmlPreview.Should().Be(preview);
        FluentActions.Invoking(entry.ValidateSource).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void OriginalChangedBeforeFirstBuild_IsRejectedInsteadOfBlessingMismatchedEvidence()
    {
        var input = Fixture(); input.Deadlocks[0].OriginalElement!.SetAttributeValue("changed", true);
        FluentActions.Invoking(() => new XelSearchService().Build([Source(input: input)]))
            .Should().Throw<InvalidDataException>().WithMessage("*已改变*");
    }

    [Fact]
    public void NamespaceNormalization_DoesNotInvalidateUnchangedOriginalEvidence()
    {
        string xml = Fixture().Document!.ToString().Replace("<deadlock>", "<deadlock xmlns=\"urn:fixture\">", StringComparison.Ordinal);
        xml.Should().Contain("urn:fixture");
        var input = new InputRecognitionService().Parse(xml);
        var catalog = new XelSearchService().Build([Source(input: input)]);
        FluentActions.Invoking(catalog.Events.Single().ValidateSource).Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RolloverFileWithoutDeadlocks_IsRetainedAndDoesNotAbortBatch(int recordCount)
    {
        using var stream = new MemoryStream(new byte[32]);
        var records = Enumerable.Range(0, recordCount).Select(i => Record("sql_batch_completed", "ordinary event", 128 + i * 100)).ToArray();
        var empty = await new XelReader(new EventSource(records)).ReadDocumentAsync(stream);
        empty.ErrorCode.Should().Be("INPUT_XEL_NO_DEADLOCK");
        var reader = new SequenceReader(empty, Fixture() with { Kind = AnalysisDocumentKind.XelDeadlockTrace });
        var service = new XelSearchService(reader);
        var catalog = await service.LoadAsync([], ["rollover.xel", "deadlock.xel"], default);
        catalog.Sources.Should().HaveCount(2); catalog.Events.Should().ContainSingle();
        catalog.SkippedRecords.Should().Be(recordCount); catalog.Notices.Should().Contain("INPUT_XEL_NO_DEADLOCK");
        var onlyEmpty = service.Build([Source(input: empty)]);
        service.Search(onlyEmpty, new()).Summary.Should().Contain("已载入 1 文件").And.Contain("匹配 0 / 0");
    }

    [Theory]
    [InlineData(InputStatus.Invalid, "INPUT_XEL_INVALID_EVENTS")]
    [InlineData(InputStatus.TooLarge, "INPUT_TOO_LARGE")]
    [InlineData(InputStatus.UnexpectedError, "INPUT_UNEXPECTED_ERROR")]
    [InlineData(InputStatus.Unsupported, "UNKNOWN_FORMAT")]
    public async Task FailedOrUnsupportedFile_StillRejectsWholeImport(InputStatus status, string code)
    {
        var failed = new InputRecognitionResult(status, AnalysisDocumentKind.XelDeadlockTrace, null, code);
        var service = new XelSearchService(new SequenceReader(failed)); var original = Catalog();
        await FluentActions.Awaiting(() => service.LoadAsync(original.Sources, ["bad.xel"], default))
            .Should().ThrowAsync<InvalidDataException>();
        original.Events.Should().HaveCount(2);
        foreach (var entry in original.Events) entry.ValidateSource();
    }

    [Fact]
    public void SymmetricGraph_UsesBoundedAllocationsInsteadOfMultiplyingLabelsByPermutations()
    {
        var input = SymmetricGraph(6, false);
        foreach (var resource in input.Deadlocks[0].Document.Descendants("keylock")) resource.SetAttributeValue("objectname", new string('x', 2048));
        var service = new XelSearchService(); service.Build([Source(input: input)]); // Warm JIT before allocation measurement.
        long before = GC.GetAllocatedBytesForCurrentThread();
        var catalog = service.Build([Source(input: input)]);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        catalog.Events.Should().ContainSingle().Which.StructureKey.Should().StartWith("structure-v2:");
        allocated.Should().BeLessThan(8L * 1024 * 1024, "a small symmetric event must not allocate hundreds of MiB");
    }

    [Fact]
    public void LargeStructuralLabels_AreIsolatedWithoutDroppingSearchOrTrace()
    {
        var input = SymmetricGraph(6, false);
        foreach (var resource in input.Deadlocks[0].Document.Descendants("keylock")) resource.SetAttributeValue("objectname", new string('x', 256 * 1024));
        var service = new XelSearchService(); var catalog = service.Build([Source(input: input), Source("copy.xel", input)]);
        var result = service.Search(catalog, new(Object: "xxxx"));
        result.Groups.Should().HaveCount(2).And.OnlyContain(g => g.Description.Contains("结构标签超过"));
        foreach (var entry in catalog.Events) entry.ValidateSource();
    }

    [Fact]
    public void LargeTopologyWork_IsIsolatedBeforeEnumeratingAllPermutations()
    {
        var input = SymmetricGraph(6, false);
        var list = input.Deadlocks[0].Document.Root!.Element("resource-list")!;
        var resources = list.Elements().ToArray();
        list.ReplaceNodes(Enumerable.Range(0, 80).SelectMany(_ => resources.Select(r => new XElement(r))));
        var catalog = new XelSearchService().Build([Source(input: input)]);
        catalog.Events.Should().ContainSingle().Which.StructureDescription.Should().Contain("2,000,000");
    }

    [Fact]
    public void StreamingHash_PreservesUtf8AcrossBufferBoundaries()
    {
        var input = SymmetricGraph(6, false);
        input.Deadlocks[0].Document.Root!.Add(new XElement("inputbuf", string.Concat(Enumerable.Repeat("汉😀", 5000))));
        var entry = new XelSearchService().Build([Source(input: input)]).Events.Single();
        entry.XmlHash.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Event.Document.ToString(SaveOptions.DisableFormatting)))));
    }

    private sealed class SequenceReader(params InputRecognitionResult[] results) : IDiagnosticDocumentReader
    {
        public int Calls { get; private set; }
        public Task<InputRecognitionResult> ReadAsync(Stream stream, DocumentReadOptions? options = null,
            CancellationToken cancellationToken = default, string? sourceName = null) => throw new NotSupportedException();
        public Task<InputRecognitionResult> ReadFileAsync(string path, DocumentReadOptions? options = null,
            CancellationToken cancellationToken = default) => Task.FromResult(results[Calls++]);
    }
}
