using System.Globalization;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class XelSearchServiceTests
{
    internal static InputRecognitionResult Fixture(string name = "imp23_search_a.xdl")
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(InputRecognitionTests.Fixture(name)));
        return new InputRecognitionService().ReadAsync(stream, sourceName: name).GetAwaiter().GetResult();
    }
    internal static XelSearchSource Source(string path = "a.xel", InputRecognitionResult? input = null) => new(path, input ?? Fixture());
    internal static XelSearchCatalog Catalog() => new XelSearchService().Build([Source(), Source("b.xel", Fixture("imp23_search_b.xdl"))]);

    [Fact]
    public void MultiFileStructure_IgnoresSpidsLocalIdsAndOrderButRetainsEveryOriginalIndex()
    {
        var service = new XelSearchService(); var catalog = Catalog(); var result = service.Search(catalog, new());
        result.Groups.Should().ContainSingle().Which.Events.Should().HaveCount(2);
        result.Events.Select(e => e.SourcePath).Should().Equal("a.xel", "b.xel");
        result.Events[0].Entry.Event.Should().BeSameAs(catalog.Sources[0].Input.Deadlocks[0]);
        result.Groups[0].Description.Should().Contain("依赖拓扑");
        result.Events.Should().OnlyContain(e => !e.IsDuplicate);
    }

    [Theory]
    [InlineData("Sales.dbo.Orders", "Sales.dbo.Other")]
    [InlineData("mode=\"S\"", "mode=\"X\"")]
    [InlineData("dbid=\"7\"", "dbid=\"8\"")]
    [InlineData("hobtid=\"1001\"", "hobtid=\"9999\"")]
    [InlineData("read committed (2)", "serializable (4)")]
    [InlineData("<victimProcess id=\"p1\"", "<victimProcess id=\"p2\"")]
    [InlineData("<owner id=\"p1\"", "<owner id=\"p2\"")]
    public void SameSpids_WithDifferentResourcesModesVictimsOrDependencies_DoNotAggregate(string oldValue, string newValue)
    {
        var original = Fixture();
        string xml = original.Document!.ToString(); xml.Should().Contain(oldValue);
        var changed = new InputRecognitionService().Parse(xml.Replace(oldValue, newValue, StringComparison.Ordinal));
        var service = new XelSearchService();
        service.Search(service.Build([Source(input: original), Source("b.xel", changed)]), new()).Groups.Should().HaveCount(2);
    }

    [Fact]
    public void TimeFilter_ConvertsOffsetsWithInclusiveTickPrecisionAndPreservesOriginalTime()
    {
        var service = new XelSearchService(); var catalog = Catalog();
        var instant = XelSearchFilter.ParseTime("2026-09-10T08:00:00.1234567+08:00");
        var result = service.Search(catalog, new(instant, instant, DisplayOffset: TimeSpan.FromHours(8)));
        result.Events.Should().ContainSingle();
        result.Events[0].DisplayTime.Should().Be("2026-09-10T08:00:00.1234567+08:00");
        result.Events[0].OriginalTime.Should().Be("2026-09-10T00:00:00.1234567+00:00");
        service.Search(catalog, new(instant!.Value.AddTicks(1), instant.Value.AddSeconds(1))).Events.Should().ContainSingle().Which.SourcePath.Should().Be("b.xel");
    }

    [Fact]
    public void RepeatedDstClockHour_UsesExplicitOffsetsInsteadOfMachineZone()
    {
        var source = Fixture(); var item = source.Deadlocks[0];
        var a = item with { CapturedAt = DateTimeOffset.Parse("2026-11-01T01:30:00-04:00", CultureInfo.InvariantCulture) };
        var b = item with { Index = 2, CapturedAt = DateTimeOffset.Parse("2026-11-01T01:30:00-05:00", CultureInfo.InvariantCulture) };
        var service = new XelSearchService(); var catalog = service.Build([Source(input: source with { Deadlocks = [b, a] })]);
        var result = service.Search(catalog, new(a.CapturedAt, a.CapturedAt));
        result.Events.Should().ContainSingle().Which.Entry.Event.Should().BeSameAs(a);
        catalog.Events.Select(e => e.Event).Should().Equal(a, b);
    }

    [Theory]
    [InlineData("2026-09-10T08:00:00")]
    [InlineData("invalid")]
    [InlineData("2026-09-10T08:00:00+25:00")]
    public void TimeWithoutValidExplicitZone_IsRejected(string value) =>
        FluentActions.Invoking(() => XelSearchFilter.ParseTime(value)).Should().Throw<InvalidDataException>();

    [Fact]
    public void UnknownTimestamp_IsLastExcludedByTimeFilterAndNeverInventedAsUtc()
    {
        var input = Fixture(); var unknown = input.Deadlocks[0] with { CapturedAt = null, Timestamp = "2026-09-10T08:00:00" };
        var service = new XelSearchService();
        var catalog = service.Build([Source(input: input with { Deadlocks = [unknown, input.Deadlocks[0]] })]);
        service.Search(catalog, new()).Events.Last().Entry.Event.Should().BeSameAs(unknown);
        service.Search(catalog, new(From: DateTimeOffset.MinValue)).Events.Should().ContainSingle();
        service.Search(catalog, new()).Summary.Should().Contain("未知时区时间 1");
    }

    [Fact]
    public async Task XelRecords_FiltersIncludeActionsAndFieldsButNotSkippedEventsAndPreserveOffsets()
    {
        string xml = Fixture().Deadlocks[0].Document.ToString();
        var records = new[] { Record("sql_batch_completed", "<not-deadlock/>", 64), Record("xml_deadlock_report", xml, 200) };
        using var stream = new MemoryStream(new byte[32]);
        var input = await new XelReader(new EventSource(records)).ReadDocumentAsync(stream, sourceName: "trace.xel");
        var service = new XelSearchService(); var catalog = service.Build([Source("trace.xel", input)]);
        var result = service.Search(catalog, new(Database: "sALES", Object: "orders", Text: "ACTION-MARKER"));
        var entry = result.Events.Should().ContainSingle().Subject.Entry;
        entry.Event.Index.Should().Be(2); entry.Event.Location!.ByteOffset.Should().Be(200); entry.Event.Location.ByteLength.Should().Be(100);
        entry.SourceHash.Should().Be(input.Envelope!.SourceHash); entry.Location.Should().Contain("/xel/event[2]");
        catalog.SkippedRecords.Should().Be(1); catalog.Notices.Should().Contain("INPUT_SKIPPED_RECORD");
        service.Search(catalog, new(Text: "field-marker")).Events.Should().ContainSingle();
        service.Search(catalog, new(Text: "search-marker")).Events.Should().ContainSingle();
        service.Search(catalog, new(Database: "missing", Text: "search-marker")).Events.Should().BeEmpty();
        service.Search(catalog, new(Text: "not-deadlock")).Events.Should().BeEmpty();
        service.Search(catalog, new(Text: "search.*marker")).Events.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateCopies_AreMarkedAndKeptWithAllSourcePaths()
    {
        var input = Fixture(); var service = new XelSearchService();
        var catalog = service.Build([Source("z.xel", input), Source("a.xel", input)]);
        var result = service.Search(catalog, new());
        result.Events.Select(e => e.SourcePath).Should().Equal("a.xel", "z.xel");
        result.Events.Select(e => e.IsDuplicate).Should().Equal(false, true);
        result.Groups.Should().ContainSingle().Which.DuplicateCount.Should().Be(1);
    }

    [Fact]
    public async Task MultiplePayloadsInOneRecord_RetainDistinctOriginalLocations()
    {
        string xml = Fixture().Deadlocks[0].Document.ToString();
        using var stream = new MemoryStream(new byte[32]);
        var input = await new XelReader(new EventSource([Record("xml_deadlock_report", "<deadlock-list>" + xml + xml + "</deadlock-list>", 128)])).ReadDocumentAsync(stream);
        var service = new XelSearchService(); var catalog = service.Build([Source(input: input)]);
        catalog.Events.Select(e => e.Event.PayloadIndex).Should().Equal(1, 2);
        catalog.Events.Select(e => e.Location).Should().OnlyHaveUniqueItems();
        catalog.SkippedRecords.Should().Be(0);
    }

    [Theory]
    [InlineData("files")]
    [InlineData("events")]
    [InlineData("bytes")]
    [InlineData("characters")]
    public void CollectionBudgets_RejectWholeResult(string budget)
    {
        var options = budget switch
        {
            "files" => new XelSearchOptions { MaxFiles = 1 },
            "events" => new XelSearchOptions { MaxEvents = 1 },
            "bytes" => new XelSearchOptions { MaxBytes = 1 },
            _ => new XelSearchOptions { MaxXmlCharacters = 1 }
        };
        var sources = new[] { Source(), Source("b.xel") };
        FluentActions.Invoking(() => new XelSearchService(options: options).Build(sources)).Should().Throw<DocumentBudgetExceededException>();
    }

    [Fact]
    public void IncompleteResourceIdentity_IsExplicitlyIsolated()
    {
        var input = Fixture(); input.Document!.Descendants("keylock").Attributes("dbid").Remove();
        var parsed = new InputRecognitionService().Parse(input.Document.ToString());
        var service = new XelSearchService(); var result = service.Search(service.Build([Source(input: parsed), Source("b.xel", parsed)]), new());
        result.Groups.Should().HaveCount(2).And.OnlyContain(g => g.Description.Contains("单独保留"));
    }

    [Fact]
    public void ChangedSource_CannotNavigateUsingOldCachedFacts()
    {
        var entry = Catalog().Events[0]; entry.Event.Document.Root!.SetAttributeValue("changed", true);
        FluentActions.Invoking(entry.ValidateSource).Should().Throw<InvalidDataException>().WithMessage("*已改变*");
    }

    [Fact]
    public void ChangedOriginalElement_KeepsPreviewSnapshotAndBlocksNavigation()
    {
        var entry = Catalog().Events[0]; string preview = entry.XmlPreview;
        entry.Event.OriginalElement!.SetAttributeValue("changed", true);
        entry.XmlPreview.Should().Be(preview).And.NotContain("changed");
        FluentActions.Invoking(entry.ValidateSource).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void CancelledSearchAndBuild_DoNotReturnPartialResults()
    {
        var service = new XelSearchService(); var catalog = Catalog(); var token = new CancellationToken(true);
        FluentActions.Invoking(() => service.Build(catalog.Sources, token)).Should().Throw<OperationCanceledException>();
        FluentActions.Invoking(() => service.Search(catalog, new(), token)).Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public async Task MultiFileLoad_UsesBudgetsAndSkipsSamePathWithoutDroppingDifferentCopies()
    {
        var reader = new Reader(); var service = new XelSearchService(reader);
        var catalog = await service.LoadAsync([], [Path.GetFullPath("b.xel"), Path.GetFullPath("a.xel"), Path.GetFullPath("A.xel")], default);
        catalog.Sources.Should().HaveCount(2); reader.Options.Should().HaveCount(2);
        reader.Options[1].MaxBytes.Should().BeLessThanOrEqualTo(reader.Options[0].MaxBytes);
        reader.Options[1].MaxXelEvents.Should().BeLessThan(reader.Options[0].MaxXelEvents);
        service.Search(catalog, new()).Groups.Should().ContainSingle();
    }

    [Fact]
    public async Task FailedFileLoad_DoesNotMutateExistingCollection()
    {
        var existing = new[] { Source(Path.GetFullPath("existing.xel")) };
        var reader = new Reader { FailAt = 2 }; var service = new XelSearchService(reader);
        await FluentActions.Awaiting(() => service.LoadAsync(existing, ["a.xel", "b.xel"], default)).Should().ThrowAsync<InvalidDataException>();
        existing.Should().ContainSingle();
    }

    [Fact]
    public void IdenticalLocalRoles_WithDifferentGlobalTopology_DoNotAggregate()
    {
        var service = new XelSearchService();
        var cycle = SymmetricGraph(6, false); var triangles = SymmetricGraph(6, true);
        var result = service.Search(service.Build([Source(input: cycle), Source("b.xel", triangles)]), new());
        result.Groups.Should().HaveCount(2).And.OnlyContain(g => !g.Key.StartsWith("isolated:"));
        var reordered = SymmetricGraph(6, false);
        foreach (var list in reordered.Deadlocks[0].Document.Root!.Elements()) list.ReplaceNodes(list.Elements().Reverse().ToArray());
        service.Search(service.Build([Source(input: cycle), Source("b.xel", reordered)]), new()).Groups.Should().ContainSingle();
    }

    [Fact]
    public void SymmetricStructureOverBudget_IsKeptSeparateInsteadOfUsingApproximateHash()
    {
        var service = new XelSearchService(); var input = SymmetricGraph(7, false);
        var result = service.Search(service.Build([Source(input: input), Source("b.xel", input)]), new());
        result.Groups.Should().HaveCount(2).And.OnlyContain(g => g.Description.Contains("720"));
    }

    [Fact]
    public async Task DifferentCapturedOrigins_DoNotAggregateOrBecomeDuplicates()
    {
        var record = Record("xml_deadlock_report", Fixture().Deadlocks[0].Document.ToString(), 128);
        var other = record with { Actions = new Dictionary<string, string>(record.Actions) { ["database_name"] = "OtherDatabase" } };
        using var a = new MemoryStream(new byte[32]); using var b = new MemoryStream(new byte[32]);
        var first = await new XelReader(new EventSource([record])).ReadDocumentAsync(a);
        var second = await new XelReader(new EventSource([other])).ReadDocumentAsync(b);
        var service = new XelSearchService(); var result = service.Search(service.Build([Source(input: first), Source("b.xel", second)]), new());
        result.Groups.Should().HaveCount(2); result.Events.Should().OnlyContain(row => !row.IsDuplicate);
    }

    [Fact]
    public void DisplayOffsetAtDateLimit_ShowsExplicitRangeNotice()
    {
        var input = Fixture(); var item = input.Deadlocks[0] with { CapturedAt = DateTimeOffset.MaxValue };
        var service = new XelSearchService(); var catalog = service.Build([Source(input: input with { Deadlocks = [item] })]);
        service.Search(catalog, new(DisplayOffset: TimeSpan.FromHours(14))).Events.Single().DisplayTime.Should().Contain("转换超出日期范围");
    }

    internal static InputRecognitionResult SymmetricGraph(int count, bool triangles)
    {
        var root = new XElement("deadlock", new XElement("victim-list"),
            new XElement("process-list", Enumerable.Range(0, count).Select(i => new XElement("process", new XAttribute("id", "p" + i), new XAttribute("spid", 50 + i)))),
            new XElement("resource-list", Enumerable.Range(0, count).Select(i => new XElement("keylock",
                new XAttribute("dbid", 7), new XAttribute("objectname", "SameResource"), new XAttribute("id", "r" + i),
                new XElement("owner-list", new XElement("owner", new XAttribute("id", "p" + i), new XAttribute("mode", "X"))),
                new XElement("waiter-list", new XElement("waiter", new XAttribute("id", "p" + (triangles ? i / 3 * 3 + (i + 1) % 3 : (i + 1) % count)), new XAttribute("mode", "S")))))));
        return Fixture() with { Deadlocks = [new DeadlockInput(1, null, new XDocument(root))] };
    }

    internal static XelInputRecord Record(string name, string xml, long offset) => new(name,
        DateTimeOffset.Parse("2026-09-10T00:00:00.1234567Z", CultureInfo.InvariantCulture), offset, 100,
        new Dictionary<string, string> { ["xml_report"] = xml, ["future"] = "field-marker" },
        new Dictionary<string, string> { ["database_name"] = "Sales", ["client_app_name"] = "action-marker" });
    internal sealed class EventSource(XelInputRecord[] records) : IXelEventSource
    {
        public async Task ReadAsync(Stream stream, Func<XelInputRecord, Task> onEvent, CancellationToken cancellationToken)
        {
            foreach (var record in records) { cancellationToken.ThrowIfCancellationRequested(); await onEvent(record); }
        }
    }
    private sealed class Reader : IDiagnosticDocumentReader
    {
        public List<DocumentReadOptions> Options { get; } = [];
        public int FailAt { get; init; }
        public Task<InputRecognitionResult> ReadAsync(Stream stream, DocumentReadOptions? options = null, CancellationToken cancellationToken = default, string? sourceName = null) => throw new NotSupportedException();
        public Task<InputRecognitionResult> ReadFileAsync(string path, DocumentReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            Options.Add(options!);
            return Task.FromResult(Options.Count == FailAt ? new InputRecognitionResult(InputStatus.Invalid, AnalysisDocumentKind.XelDeadlockTrace, null, "INPUT_INVALID_XEL", "坏文件")
                : Fixture() with { Kind = AnalysisDocumentKind.XelDeadlockTrace });
        }
    }
}
