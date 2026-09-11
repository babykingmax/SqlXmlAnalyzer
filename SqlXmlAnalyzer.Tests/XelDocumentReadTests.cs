using System.IO;
using System.Text;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class XelDocumentReadTests
{
    private const string Deadlock = "<deadlock><process-list><process id='p1'/></process-list><resource-list><keylock/></resource-list></deadlock>";

    [Fact]
    public async Task MixedEvents_RetainOffsetsTimeZoneUnknownFieldsAndSkippedRanges()
    {
        var source = new FakeSource(Record("other", "unused", 0), Record("xml_deadlock_report", Deadlock, 100),
            Record("xml_deadlock_report", "<deadlock/>", 200), Record("xml_deadlock_report", Deadlock, 300));
        using var stream = new MemoryStream(new byte[512]);
        var result = await new XelReader(source).ReadDocumentAsync(stream, sourceName: "events.xel");
        result.Status.Should().Be(InputStatus.Partial);
        result.IsSuccess.Should().BeFalse();
        result.HasUsableContent.Should().BeTrue();
        result.Deadlocks.Select(e => e.Index).Should().Equal(2, 4);
        result.Deadlocks[1].Location!.ByteOffset.Should().Be(300);
        result.Deadlocks[1].Location!.ByteLength.Should().Be(100);
        result.Deadlocks[1].CapturedAt!.Value.Offset.Should().Be(TimeSpan.FromHours(8));
        result.Deadlocks[1].Timestamp.Should().EndWith("+08:00");
        result.Diagnostics.Select(d => d.Location!.EventIndex).Should().Equal(1, 3);
        result.XelRecords.Should().HaveCount(4);
        result.XelRecords[0].Actions["future"].Should().Be("opaque-action");
        result.XelRecords[1].Fields["future"].Should().Be("opaque-field");
        result.Envelope!.SourceBytes.Should().Be(512);
        result.Envelope.SourceHash.Should().NotBeNullOrWhiteSpace();
        result.SourceSnapshot.Length.Should().Be(512);
        stream.CanRead.Should().BeTrue();
    }

    [Theory]
    [InlineData("XelEvents")]
    [InlineData("XmlCharacters")]
    [InlineData("Nodes")]
    [InlineData("Depth")]
    [InlineData("Bytes")]
    public async Task BudgetExceeded_NeverPublishesEarlierEventsAsComplete(string budget)
    {
        var options = budget switch
        {
            "XelEvents" => new DocumentReadOptions { MaxXelEvents = 1 },
            "XmlCharacters" => new DocumentReadOptions { MaxXmlCharacters = Deadlock.Length + 1 },
            "Nodes" => new DocumentReadOptions { MaxNodes = 20 },
            "Depth" => new DocumentReadOptions { MaxDepth = 2 },
            _ => new DocumentReadOptions { MaxBytes = 2 }
        };
        using var stream = new MemoryStream(new byte[32]);
        var reporter = new Reporter();
        var source = new FakeSource(Record("xml_deadlock_report", Deadlock, 0), Record("xml_deadlock_report", Deadlock, 100));
        var result = await new XelReader(source).ReadDocumentAsync(stream, options, unexpectedErrors: reporter);
        result.Status.Should().Be(InputStatus.TooLarge);
        result.Deadlocks.Should().BeEmpty();
        result.IsSuccess.Should().BeFalse();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task EventBudget_CountsAllEventsIncludingUnrelatedRecords()
    {
        using var stream = new MemoryStream(new byte[32]);
        var source = new FakeSource(Record("other", "", 0), Record("xml_deadlock_report", Deadlock, 100));
        var result = await new XelReader(source).ReadDocumentAsync(stream, new DocumentReadOptions { MaxXelEvents = 1 });
        result.Status.Should().Be(InputStatus.TooLarge);
        result.Deadlocks.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationAfterEvent_DiscardsIncompleteResultWithoutDump()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new MemoryStream(new byte[32]);
        var reporter = new Reporter();
        var source = new FakeSource(Record("xml_deadlock_report", Deadlock, 0)) { AfterEvent = cancellation.Cancel };
        var result = await new XelReader(source).ReadDocumentAsync(stream, cancellationToken: cancellation.Token, unexpectedErrors: reporter);
        result.Status.Should().Be(InputStatus.Cancelled);
        result.Deadlocks.Should().BeEmpty();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task UnknownStreamerError_IsReportedOnceAndDoesNotPublishCollectedEvents()
    {
        using var stream = new MemoryStream(new byte[32]);
        var reporter = new Reporter();
        var source = new FakeSource(Record("xml_deadlock_report", Deadlock, 0)) { AfterEvent = () => throw new InvalidOperationException("synthetic unknown") };
        var result = await new XelReader(source).ReadDocumentAsync(stream, unexpectedErrors: reporter);
        result.Status.Should().Be(InputStatus.UnexpectedError);
        result.ErrorMessage.Should().Contain("DUMP");
        result.Deadlocks.Should().BeEmpty();
        reporter.Calls.Should().Be(1);
        reporter.Operation.Should().Be("XelReader.Read");
    }

    [Fact]
    public async Task EmptyXel_ReturnsUnsupportedInsteadOfZeroProblemSuccess()
    {
        using var stream = new MemoryStream(new byte[32]);
        var result = await new XelReader(new FakeSource()).ReadDocumentAsync(stream);
        result.Status.Should().Be(InputStatus.Unsupported);
        result.ErrorCode.Should().Be("INPUT_XEL_NO_DEADLOCK");
        result.HasUsableContent.Should().BeFalse();
    }

    [Fact]
    public async Task TruncatedRealXelStream_ReturnsInvalidWithoutDump()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a trace"));
        var reporter = new Reporter();
        var result = await new XelReader().ReadDocumentAsync(stream, unexpectedErrors: reporter);
        result.Status.Should().Be(InputStatus.Invalid);
        result.ErrorCode.Should().Be("INPUT_INVALID_XEL");
        reporter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(1024)]
    [InlineData(65536)]
    public async Task InvalidBinaryXel_ReturnsInvalidWithoutDump(int size)
    {
        using var stream = new MemoryStream(new byte[size]);
        var reporter = new Reporter();
        var result = await new XelReader().ReadDocumentAsync(stream, unexpectedErrors: reporter);
        result.Status.Should().Be(InputStatus.Invalid);
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AllMalformedPayloads_ReturnInvalidWithEverySkippedLocation()
    {
        using var stream = new MemoryStream(new byte[32]);
        var source = new FakeSource(Record("xml_deadlock_report", "<deadlock/>", 0),
            Record("xml_deadlock_report", "<broken", 100));
        var result = await new XelReader(source).ReadDocumentAsync(stream);
        result.Status.Should().Be(InputStatus.Invalid);
        result.Diagnostics.Select(d => d.Location!.EventIndex).Should().Equal(1, 2);
    }

    [Fact]
    public async Task XmlPayloadWithDtd_IsExplicitlySkippedAndNeverResolved()
    {
        using var stream = new MemoryStream(new byte[32]);
        var source = new FakeSource(Record("xml_deadlock_report", Deadlock, 0),
            Record("xml_deadlock_report", "<!DOCTYPE a [<!ENTITY secret SYSTEM 'file:///must-not-read'>]><a>&secret;</a>", 100));
        var result = await new XelReader(source).ReadDocumentAsync(stream);
        result.Status.Should().Be(InputStatus.Partial);
        result.Diagnostics[0].Code.Should().Be("INPUT_MALFORMED_XML");
        result.Diagnostics[0].Location!.EventIndex.Should().Be(2);
        result.XelRecords[1].Fields["xml_report"].Should().Contain("DOCTYPE");
    }

    private static XelInputRecord Record(string name, string xml, long offset) => new(name,
        new DateTimeOffset(2026, 9, 8, 8, 0, 0, TimeSpan.FromHours(8)), offset, 100,
        new Dictionary<string, string> { ["xml_report"] = xml, ["future"] = "opaque-field" },
        new Dictionary<string, string> { ["future"] = "opaque-action" });

    [Fact]
    public async Task MultipleDeadlocksInsideOneXelRecord_HaveDistinctPayloadLocationsAndLabels()
    {
        using var stream = new MemoryStream(new byte[32]);
        var source = new FakeSource(Record("xml_deadlock_report", "<deadlock-list>" + Deadlock + Deadlock + "</deadlock-list>", 100));
        var result = await new XelReader(source).ReadDocumentAsync(stream);
        result.IsSuccess.Should().BeTrue();
        result.Deadlocks.Select(e => e.Index).Should().Equal(1, 1);
        result.Deadlocks.Select(e => e.PayloadIndex).Should().Equal(1, 2);
        result.Deadlocks.Select(e => e.Location!.XmlPath).Should().OnlyHaveUniqueItems();
        result.Deadlocks.Select(e => e.DisplayName).Should().OnlyHaveUniqueItems();
        result.Envelope!.CapturedAt.Should().BeNull();
    }

    private sealed class FakeSource(params XelInputRecord[] records) : IXelEventSource
    {
        public Action? AfterEvent { get; init; }
        public async Task ReadAsync(Stream stream, Func<XelInputRecord, Task> onEvent, CancellationToken cancellationToken)
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await onEvent(record);
                AfterEvent?.Invoke();
            }
        }
    }

    private sealed class Reporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public string? Operation { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Calls++;
            Operation = operation;
            return new(null, null, "test diagnostic writer");
        }
    }
}
