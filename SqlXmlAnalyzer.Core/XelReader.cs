using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml;
using Microsoft.SqlServer.XEvent.XELite;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core;

public class XelDeadlockReport
{
    public string Timestamp { get; set; } = string.Empty;
    public string DeadlockXml { get; set; } = string.Empty;
}

public sealed record XelInputRecord(string Name, DateTimeOffset Timestamp, long Offset, long Length,
    IReadOnlyDictionary<string, string> Fields, IReadOnlyDictionary<string, string> Actions);

public interface IXelEventSource
{
    Task ReadAsync(Stream stream, Func<XelInputRecord, Task> onEvent, CancellationToken cancellationToken);
}

internal sealed class XelEventSource : IXelEventSource
{
    public async Task ReadAsync(Stream stream, Func<XelInputRecord, Task> onEvent, CancellationToken cancellationToken)
    {
        if (stream.Length < 16) throw new InvalidDataException("XEL header is truncated.");
        try
        {
            await new XEFileEventStreamer(stream).ReadEventStream(e => onEvent(new(e.Name, e.Timestamp,
                e.XEventStartOffsetInBytes, e.XEventSizeInBytes, Copy(e.Fields), Copy(e.Actions))), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception) when (exception is not DocumentBudgetExceededException)
        {
            // The parser reads our in-memory snapshot: IO here means invalid XEL contents,
            // while IO during the earlier file/stream snapshot remains a ReadError.
            throw new InvalidDataException("XEL snapshot could not be decoded.", exception);
        }
    }

    private static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, object> values)
        => new ReadOnlyDictionary<string, string>(values.ToDictionary(p => p.Key,
            p => Convert.ToString(p.Value, CultureInfo.InvariantCulture) ?? string.Empty));
}

public class XelReader
{
    private readonly IXelEventSource _source;
    public XelReader(IXelEventSource? source = null) => _source = source ?? new XelEventSource();

    public async Task<InputRecognitionResult> ReadDocumentAsync(string path, DocumentReadOptions? options = null,
        CancellationToken cancellationToken = default, IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await ReadDocumentAsync(stream, options, cancellationToken, path, unexpectedErrors).ConfigureAwait(false);
        }
        catch (Exception exception) { return Failure(exception, cancellationToken, unexpectedErrors); }
    }

    public async Task<InputRecognitionResult> ReadDocumentAsync(Stream stream, DocumentReadOptions? options = null,
        CancellationToken cancellationToken = default, string? sourceName = null, IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        options ??= new DocumentReadOptions();
        options.Validate();
        var events = new List<DeadlockInput>();
        var records = new List<XelInputRecord>();
        var diagnostics = new List<InputDiagnostic>();
        try
        {
            Logger.Debug("XelReader: 开始读取有预算的输入快照。");
            byte[] bytes = await ReadSnapshotAsync(stream, options, cancellationToken).ConfigureAwait(false);
            using var snapshot = new MemoryStream(bytes, writable: false);
            long characters = 0, nodes = 0;
            await _source.ReadAsync(snapshot, record =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (records.Count >= options.MaxXelEvents) throw new DocumentBudgetExceededException("XelEvents");
                records.Add(record);
                int index = records.Count;
                var location = new SourceLocation($"/xel/event[{index}]", null, null, index)
                    { ByteOffset = record.Offset, ByteLength = record.Length };
                if (record.Name != "xml_deadlock_report" || !record.Fields.TryGetValue("xml_report", out string? xml))
                {
                    diagnostics.Add(new("INPUT_SKIPPED_RECORD", $"未处理 XEL 事件 {index}：不是受支持的死锁事件或缺少 xml_report。", location));
                    return Task.CompletedTask;
                }
                characters += xml.Length;
                if (characters > options.MaxXmlCharacters) throw new DocumentBudgetExceededException("XmlCharacters");
                var input = new InputRecognitionService(unexpectedErrors, options: options).Parse(xml, cancellationToken);
                if (input.Status == InputStatus.TooLarge) throw new DocumentBudgetExceededException("XML event");
                if (input.Status == InputStatus.UnexpectedError) throw new XelInputFailureException(input);
                if (input.Document != null)
                {
                    // Charge a conservative count including end tags, attributes and non-element nodes.
                    nodes += input.Document.DescendantNodes().LongCount() + input.Document.Descendants().Sum(e => 1L + e.Attributes().LongCount());
                    if (nodes > options.MaxNodes) throw new DocumentBudgetExceededException("Nodes");
                }
                if (!input.HasUsableContent || input.Kind != AnalysisDocumentKind.DeadlockXml)
                    diagnostics.Add(new(input.ErrorCode ?? "INPUT_EXPECTED_DEADLOCK", $"未处理 XEL 事件 {index}：无有效死锁。", location));
                else
                {
                    foreach (var item in input.Deadlocks)
                        events.Add(item with
                        {
                            Index = index,
                            PayloadIndex = item.Index,
                            Timestamp = record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                            CapturedAt = record.Timestamp,
                            Location = location with { XmlPath = location.XmlPath + item.Location?.XmlPath },
                            CaptureFields = record.Fields
                        });
                    diagnostics.AddRange(input.Diagnostics.Select(d => d with { Location = location }));
                }
                return Task.CompletedTask;
            }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var status = events.Count == 0
                ? records.Any(r => r.Name == "xml_deadlock_report") ? InputStatus.Invalid : InputStatus.Unsupported
                : diagnostics.Count == 0 ? InputStatus.Success : InputStatus.Partial;
            string? code = status == InputStatus.Unsupported ? "INPUT_XEL_NO_DEADLOCK" : status == InputStatus.Invalid ? "INPUT_XEL_INVALID_EVENTS"
                : status == InputStatus.Partial ? "INPUT_PARTIAL" : null;
            string? message = status == InputStatus.Success ? null : $"XEL 有效死锁 {events.Count}；未处理 {diagnostics.Count} 条记录，范围见 Diagnostics。";
            if (status == InputStatus.Partial) Logger.Warning(message!);
            if (status is InputStatus.Unsupported or InputStatus.Invalid) Logger.Error($"{code}: {message}");
            var result = new InputRecognitionResult(status, AnalysisDocumentKind.XelDeadlockTrace, null, code, message)
            {
                Deadlocks = events.AsReadOnly(), XelRecords = records.AsReadOnly(), Diagnostics = diagnostics.AsReadOnly(),
                Capabilities = DocumentCapabilities.PreservedSource | (events.Count == 0 ? DocumentCapabilities.None :
                    DocumentCapabilities.DeadlockGraph | DocumentCapabilities.OffsetTimestamps) |
                    (events.Count > 1 ? DocumentCapabilities.MultipleEvents : DocumentCapabilities.None)
            };
            return InputRecognitionService.WithSource(result, bytes, sourceName);
        }
        catch (XelInputFailureException exception) { return exception.Result; }
        catch (Exception exception) { return Failure(exception, cancellationToken, unexpectedErrors); }
    }

    private static async Task<byte[]> ReadSnapshotAsync(Stream stream, DocumentReadOptions options, CancellationToken token)
    {
        using var snapshot = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, options.MaxBytes - snapshot.Length + 1);
            int read = await stream.ReadAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            if (read == 0) break;
            if (snapshot.Length + read > options.MaxBytes) throw new DocumentBudgetExceededException("Bytes");
            snapshot.Write(buffer, 0, read);
        }
        return snapshot.ToArray();
    }

    private static InputRecognitionResult Failure(Exception exception, CancellationToken token, IUnexpectedErrorReporter? reporter)
    {
        if (exception is OperationCanceledException && token.IsCancellationRequested) return InputRecognitionService.Cancelled();
        if (exception is DocumentBudgetExceededException)
            return InputRecognitionService.Failure(InputStatus.TooLarge, AnalysisDocumentKind.XelDeadlockTrace, null, "INPUT_TOO_LARGE", exception.Message);
        if (exception is BufferChecksumVerificationException or XmlException or InvalidDataException or EndOfStreamException ||
            exception.GetType().FullName == "Microsoft.SqlServer.XEvent.XELite.XENotRealBufferException")
            return InputRecognitionService.Failure(InputStatus.Invalid, AnalysisDocumentKind.XelDeadlockTrace, null, "INPUT_INVALID_XEL", "XEL 内容损坏或格式无效，未提交事件。");
        if (exception is IOException or UnauthorizedAccessException)
            return InputRecognitionService.Failure(InputStatus.ReadError, AnalysisDocumentKind.XelDeadlockTrace, null, "INPUT_READ_ERROR", InputRecognitionService.ReadErrorMessage);
        var incident = ExceptionPolicy.Capture(exception, "XelReader.Read", reporter);
        return InputRecognitionService.Failure(InputStatus.UnexpectedError, AnalysisDocumentKind.XelDeadlockTrace, null, "INPUT_UNEXPECTED_ERROR", $"XEL 读取发生未知错误；{incident.Summary}");
    }

    // Compatibility adapter: partial results must not silently appear complete to old callers.
    public async Task<List<XelDeadlockReport>> ReadDeadlocksAsync(string xelFilePath, CancellationToken cancellationToken = default)
    {
        var result = await ReadDocumentAsync(xelFilePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Status == InputStatus.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (!result.IsSuccess) throw new InvalidDataException($"{result.ErrorCode}: {result.ErrorMessage}");
        return result.Deadlocks.Select(e => new XelDeadlockReport { Timestamp = e.Timestamp ?? "", DeadlockXml = e.Document.ToString() }).ToList();
    }

    private sealed class XelInputFailureException(InputRecognitionResult result) : Exception
    {
        public InputRecognitionResult Result { get; } = result;
    }
}
