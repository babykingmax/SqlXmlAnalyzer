using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record XelSearchSource(string Path, InputRecognitionResult Input)
{
    private readonly object _snapshotGate = new();
    private SourceBaseline? _baseline;

    // A source keeps its first accepted integrity baseline across imports and catalog rebuilds.
    // IReadOnlyList/ReadOnlyMemory do not make the underlying XML or dictionaries immutable.
    internal void ValidateSnapshot(CancellationToken token = default)
    {
        lock (_snapshotGate)
        {
            token.ThrowIfCancellationRequested();
            var members = Input.Deadlocks.ToArray();
            if (_baseline != null && (!ReferenceEquals(_baseline.Input, Input) ||
                members.Length != _baseline.Members.Length ||
                members.Where((item, i) => !ReferenceEquals(item, _baseline.Members[i])).Any()))
                throw Changed();

            IEnumerable<string> Parts()
            {
                yield return Convert.ToHexString(SHA256.HashData(Input.SourceSnapshot.Span));
                foreach (var item in members)
                {
                    token.ThrowIfCancellationRequested();
                    if (_baseline == null && item.OriginalElement != null)
                    {
                        // Match the reader's normalization, while retaining raw namespace evidence.
                        var original = new XElement(item.OriginalElement);
                        foreach (var element in original.DescendantsAndSelf())
                        {
                            token.ThrowIfCancellationRequested();
                            element.Name = element.Name.LocalName;
                            element.Attributes().Where(a => a.IsNamespaceDeclaration).Remove();
                        }
                        if (!XNode.DeepEquals(item.Document.Root, original)) throw Changed();
                    }
                    yield return "event";
                    yield return item.Document.ToString(SaveOptions.DisableFormatting);
                    yield return item.OriginalElement?.ToString(SaveOptions.DisableFormatting) ?? "";
                    yield return XelStructureKey.HashParts(Metadata(item.CaptureFields));
                }
                foreach (var record in Input.XelRecords)
                {
                    token.ThrowIfCancellationRequested();
                    yield return "record";
                    yield return record.Name;
                    yield return record.Timestamp.ToString("O", CultureInfo.InvariantCulture);
                    yield return record.Offset.ToString(CultureInfo.InvariantCulture);
                    yield return record.Length.ToString(CultureInfo.InvariantCulture);
                    yield return XelStructureKey.HashParts(Metadata(record.Fields));
                    yield return XelStructureKey.HashParts(Metadata(record.Actions));
                }
            }
            string digest = XelStructureKey.HashParts(Parts());
            token.ThrowIfCancellationRequested();
            if (_baseline == null) _baseline = new(Input, members, digest);
            else if (_baseline.Digest != digest) throw Changed();
        }
    }

    internal static IEnumerable<string> Metadata(IEnumerable<KeyValuePair<string, string>> values) => values
        .OrderBy(p => p.Key, StringComparer.Ordinal).ThenBy(p => p.Value, StringComparer.Ordinal)
        .SelectMany(p => new[] { p.Key, p.Value });
    private static InvalidDataException Changed() => new("事件快照已改变，请重新载入后追溯。");
    private sealed record SourceBaseline(InputRecognitionResult Input, DeadlockInput[] Members, string Digest);
}

public sealed record XelSearchOptions
{
    public int MaxFiles { get; init; } = 32;
    public int MaxEvents { get; init; } = 10_000;
    public long MaxBytes { get; init; } = 128L * 1024 * 1024;
    public long MaxXmlCharacters { get; init; } = 32L * 1024 * 1024;
}

public sealed record XelSearchFilter(DateTimeOffset? From = null, DateTimeOffset? To = null,
    string Database = "", string Object = "", string Text = "", TimeSpan DisplayOffset = default)
{
    public static DateTimeOffset? ParseTime(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DocumentSource.Timestamp(text.Trim())
            ?? throw new InvalidDataException("时间须包含明确时区，例如 2026-09-10T08:00:00.1234567+08:00 或 Z。");
    }
}

public sealed record XelSearchEvent(XelSearchSource Source, DeadlockInput Event, string XmlHash,
    string StructureKey, string StructureDescription, string Databases, string Objects, string SearchText,
    string DuplicateKey)
{
    public string XmlPreview { get; init; } = "";
    public string CapturePreview { get; init; } = "";
    public string? OriginalXmlHash { get; init; }
    public string SourcePath => Source.Path;
    public string SourceHash => Source.Input.Envelope?.SourceHash ?? "未采集";
    public string OriginalTime => Event.Timestamp ?? "未采集";
    public string EventLabel => $"事件 {Event.Index} / 子事件 {Event.PayloadIndex}";
    public string Location => $"{Event.Location?.XmlPath ?? "未采集 XML 位置"}；字节 {Event.Location?.ByteOffset?.ToString(CultureInfo.InvariantCulture) ?? "未采集"}；长度 {Event.Location?.ByteLength?.ToString(CultureInfo.InvariantCulture) ?? "未采集"}";

    public void ValidateSource()
    {
        Source.ValidateSnapshot();
        if (!Source.Input.Deadlocks.Any(item => ReferenceEquals(item, Event)) ||
            XelStructureKey.Hash(Event.Document.ToString(SaveOptions.DisableFormatting)) != XmlHash ||
            (OriginalXmlHash != null && (Event.OriginalElement == null ||
                XelStructureKey.Hash(Event.OriginalElement.ToString(SaveOptions.DisableFormatting)) != OriginalXmlHash)))
            throw new InvalidDataException("事件快照已改变，请重新载入后追溯。");
    }
}

public sealed record XelSearchRow(XelSearchEvent Entry, string DisplayTime, bool IsDuplicate)
{
    public string OriginalTime => Entry.OriginalTime;
    public string SourcePath => Entry.SourcePath;
    public string EventLabel => Entry.EventLabel;
    public string Databases => Entry.Databases;
    public string Objects => Entry.Objects;
    public string DuplicateLabel => IsDuplicate ? "重复副本（保留）" : "";
}

public sealed record XelSearchGroup(string Key, string Description, IReadOnlyList<XelSearchRow> Events)
{
    public int Count => Events.Count;
    public int DuplicateCount => Events.Count(row => row.IsDuplicate);
}

public sealed record XelSearchCatalog(IReadOnlyList<XelSearchSource> Sources, IReadOnlyList<XelSearchEvent> Events,
    int SkippedRecords, string Notices);
public sealed record XelSearchResult(IReadOnlyList<XelSearchRow> Events, IReadOnlyList<XelSearchGroup> Groups, string Summary);
