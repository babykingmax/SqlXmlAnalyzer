using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

/// <summary>Budgets apply to one input snapshot, not to each event independently.</summary>
public sealed record DocumentReadOptions
{
    public long MaxBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxXmlCharacters { get; init; } = 32L * 1024 * 1024;
    public int MaxDepth { get; init; } = 128;
    public long MaxNodes { get; init; } = 1_000_000;
    public int MaxXelEvents { get; init; } = 10_000;

    public void Validate()
    {
        if (MaxBytes <= 0 || MaxBytes > int.MaxValue || MaxXmlCharacters <= 0 ||
            MaxDepth <= 0 || MaxNodes <= 0 || MaxXelEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(DocumentReadOptions), "读取预算必须为正数，字节预算不能超过 Int32.MaxValue。");
    }
}

[Flags]
public enum DocumentCapabilities
{
    None = 0,
    PlanStatements = 1,
    PlanOperators = 2,
    RuntimeCounters = 4,
    DeadlockGraph = 8,
    MultipleEvents = 16,
    OffsetTimestamps = 32,
    PreservedSource = 64
}

public sealed record SourceLocation(string XmlPath, int? Line, int? Column, int? EventIndex = null)
{
    public long? ByteOffset { get; init; }
    public long? ByteLength { get; init; }
}
public sealed record InputDiagnostic(string Code, string Message, SourceLocation? Location = null);

public sealed record DocumentEnvelope(
    string DocumentId,
    string? SourceHash,
    string? SourceName,
    AnalysisDocumentKind DetectedKind,
    DateTimeOffset? CapturedAt,
    string? EngineBuild,
    string? SchemaVersion,
    long? SourceBytes);

public interface IDiagnosticDocumentReader
{
    // The caller owns the stream. XML reads support non-seekable streams.
    Task<InputRecognitionResult> ReadAsync(Stream stream, DocumentReadOptions? options = null,
        CancellationToken cancellationToken = default, string? sourceName = null);
    Task<InputRecognitionResult> ReadFileAsync(string path, DocumentReadOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class DocumentBudgetExceededException(string budget) : IOException($"输入超过 {budget} 读取预算。")
{
    public string Budget { get; } = budget;
}

internal sealed class DocumentSource(CancellationToken cancellationToken)
{
    // Scoped to one recognition, so no cached position survives source mutation or
    // keeps a previously opened document alive. Index each sibling list once.
    private readonly Dictionary<XElement, int> _siblingOrdinals = new();

    public SourceLocation Location(XElement element, int? eventIndex = null)
    {
        var path = new Stack<string>();
        foreach (XElement node in element.AncestorsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            path.Push($"{node.Name}[{SiblingOrdinal(node)}]");
        }
        var line = (System.Xml.IXmlLineInfo)element;
        return new("/" + string.Join("/", path), line.HasLineInfo() ? line.LineNumber : null,
            line.HasLineInfo() ? line.LinePosition : null, eventIndex);
    }

    private int SiblingOrdinal(XElement node)
    {
        if (node.Parent == null) return 1;
        if (_siblingOrdinals.TryGetValue(node, out int ordinal)) return ordinal;
        var counts = new Dictionary<XName, int>();
        foreach (XElement sibling in node.Parent.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.TryGetValue(sibling.Name, out int count);
            counts[sibling.Name] = count + 1;
            _siblingOrdinals[sibling] = count + 1;
        }
        return _siblingOrdinals[node];
    }

    public static DateTimeOffset? Timestamp(string? value)
    {
        // A local timestamp has no provable offset. Never infer the machine's time zone.
        if (value == null || !System.Text.RegularExpressions.Regex.IsMatch(value,
            @"(?:Z|[+-]\d{2}:\d{2})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return null;
        return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var timestamp) ? timestamp : null;
    }
}
