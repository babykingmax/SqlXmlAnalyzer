using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Reporting;

public static class DiagnosticReportLimits
{
    public const int MaxContentCharacters = 8_000_000;
    public const int MaxRecords = 100_000;
    // Encoding can expand a character to six JSON/HTML characters. This is a separate wire-format limit.
    public const int MaxEncodedLength = 64_000_000;
    internal static ReportBudgetExceededException Exceeded() => new();
}

public sealed class ReportBudgetExceededException() : IOException("REPORT_BUDGET_EXCEEDED: 报告超过大小预算（800 万内容字符或 10 万条记录），请缩小选择范围。");

/// <summary>Charge before retaining each record, including records produced by lazy projections.</summary>
internal sealed class ReportSizeBudget(CancellationToken token = default)
{
    private int _characters = 2048; // Fixed headings and model metadata.
    private int _records;
    internal int Remaining => DiagnosticReportLimits.MaxContentCharacters - _characters;
    internal void Value(string value)
    {
        token.ThrowIfCancellationRequested();
        if (value.Length > Remaining) throw DiagnosticReportLimits.Exceeded();
        _characters += value.Length;
    }
    private void Record(int overhead)
    {
        token.ThrowIfCancellationRequested();
        if (_records >= DiagnosticReportLimits.MaxRecords || overhead > Remaining) throw DiagnosticReportLimits.Exceeded();
        _records++; _characters += overhead;
    }
    internal void Field(ReportField field) { Record(8); Value(field.Name); Value(field.Value); Value(field.Category); }
    internal void Node(ReportNode node) { Record(8); Value(node.Id); Value(node.Label); }
    internal void Edge(ReportEdge edge) { Record(8); Value(edge.From); Value(edge.To); }
    internal void ItemHeader(ReportItem item)
    {
        Record(32); Value(item.Id); Value(item.RuleId); Value(item.RuleVersion); Value(item.Location);
        Value(item.Status); Value(item.Severity); Value(item.Confidence);
    }
    internal void Item(ReportItem item) { ItemHeader(item); foreach (var field in item.Fields) Field(field); }
    internal Collection<T> Collection<T>(Action<T> charge) => new BudgetedCollection<T>(charge);
    internal Collection<ReportField> Fields(IEnumerable<ReportField>? source = null)
    {
        var fields = Collection<ReportField>(Field);
        if (source != null) foreach (var field in source) fields.Add(field);
        return fields;
    }
    internal ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> source, Action<T> charge)
    {
        var list = new List<T>();
        foreach (var item in source) { charge(item); list.Add(item); }
        return Array.AsReadOnly(list.ToArray());
    }
    internal JsonDocument JsonDocument<T>(T value)
    {
        var bytes = new ReportByteBuffer(Math.Min(DiagnosticReportLimits.MaxEncodedLength, Remaining * 6), token);
        using (var writer = new Utf8JsonWriter(bytes)) JsonSerializer.Serialize(writer, value);
        return System.Text.Json.JsonDocument.Parse(bytes.WrittenMemory);
    }
    internal string Json<T>(T value)
    {
        using var document = JsonDocument(value);
        return document.RootElement.GetRawText();
    }
    internal string Xml(XNode node, bool indent = true)
    {
        using var text = new ReportTextWriter(Remaining, token);
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = indent }))
            node.WriteTo(writer);
        string xml = text.ToString(); Value(xml); return xml;
    }
    private sealed class BudgetedCollection<T>(Action<T> charge) : Collection<T>
    {
        protected override void InsertItem(int index, T item) { charge(item); base.InsertItem(index, item); }
        protected override void SetItem(int index, T item) => throw new NotSupportedException();
    }
}

/// <summary>StringBuilder.MaxCapacity alone is not a hard limit for repeated small appends.</summary>
internal sealed class ReportTextWriter(int limit, CancellationToken token = default) : TextWriter
{
    private readonly StringBuilder _text = new();
    public override Encoding Encoding => Encoding.Unicode;
    public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;
    private void Check(int count)
    {
        token.ThrowIfCancellationRequested();
        if (count > limit - _text.Length) throw DiagnosticReportLimits.Exceeded();
    }
    public override void Write(char value) { Check(1); _text.Append(value); }
    public override void Write(string? value) { if (value != null) { Check(value.Length); _text.Append(value); } }
    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));
    public override void Write(ReadOnlySpan<char> value) { Check(value.Length); _text.Append(value); }
    public override string ToString() => _text.ToString();
}

/// <summary>Reject serializer buffer requests before allocating an oversized backing array.</summary>
internal sealed class ReportByteBuffer(int limit, CancellationToken token = default) : IBufferWriter<byte>
{
    private byte[] _buffer = [];
    private int _written;
    internal ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);
    public void Advance(int count)
    {
        token.ThrowIfCancellationRequested();
        if (count < 0 || count > _buffer.Length - _written) throw new ArgumentOutOfRangeException(nameof(count));
        _written += count;
    }
    private void Ensure(int sizeHint)
    {
        token.ThrowIfCancellationRequested();
        if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
        sizeHint = Math.Max(1, sizeHint);
        if (sizeHint > limit - _written) throw DiagnosticReportLimits.Exceeded();
        if (sizeHint > _buffer.Length - _written)
            Array.Resize(ref _buffer, (int)Math.Min(limit, Math.Max((long)_written + sizeHint, Math.Max(256L, (long)_buffer.Length * 2))));
    }
    public Memory<byte> GetMemory(int sizeHint = 0) { Ensure(sizeHint); return _buffer.AsMemory(_written); }
    public Span<byte> GetSpan(int sizeHint = 0) { Ensure(sizeHint); return _buffer.AsSpan(_written); }
}
