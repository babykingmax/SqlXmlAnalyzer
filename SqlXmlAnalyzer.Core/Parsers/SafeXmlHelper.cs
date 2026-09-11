using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer;

public static class SafeXmlHelper
{
    public static XDocument LoadSafe(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        using var stream = File.OpenRead(filePath);
        return LoadSafe(stream);
    }

    public static XDocument LoadSafe(Stream stream) => LoadSafe(stream, new DocumentReadOptions());

    public static XDocument LoadSafe(Stream stream, DocumentReadOptions options, CancellationToken cancellationToken = default)
        => LoadSnapshot(ReadSnapshot(stream, options, cancellationToken), options, cancellationToken);

    internal static byte[] ReadSnapshot(Stream stream, DocumentReadOptions options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options.Validate();
        using var snapshot = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, options.MaxBytes - snapshot.Length + 1));
            token.ThrowIfCancellationRequested();
            if (read == 0) break;
            if (snapshot.Length + read > options.MaxBytes) throw new DocumentBudgetExceededException("Bytes");
            snapshot.Write(buffer, 0, read);
        }
        return snapshot.ToArray();
    }

    internal static XDocument LoadSnapshot(byte[] bytes, DocumentReadOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (bytes.LongLength > options.MaxBytes) throw new DocumentBudgetExceededException("Bytes");
        (Encoding encoding, int offset) = DetectEncoding(bytes, options, token);
        int characters = encoding.GetCharCount(bytes, offset, bytes.Length - offset);
        token.ThrowIfCancellationRequested();
        if (characters > options.MaxXmlCharacters) throw new DocumentBudgetExceededException("XmlCharacters");
        string xml = encoding.GetString(bytes, offset, bytes.Length - offset);
        return ParseSafe(xml, options, token);
    }

    private static (Encoding Encoding, int Offset) DetectEncoding(byte[] bytes, DocumentReadOptions options, CancellationToken token)
    {
        ReadOnlySpan<byte> prefix = bytes;
        if (prefix.StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) return (new UTF32Encoding(false, false, true), 4);
        if (prefix.StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) return (new UTF32Encoding(true, false, true), 4);
        if (prefix.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return (new UTF8Encoding(false, true), 3);
        if (prefix.StartsWith(new byte[] { 0xFF, 0xFE })) return (new UnicodeEncoding(false, false, true), 2);
        if (prefix.StartsWith(new byte[] { 0xFE, 0xFF })) return (new UnicodeEncoding(true, false, true), 2);
        if (prefix.StartsWith(new byte[] { 0x3C, 0, 0, 0 })) return (new UTF32Encoding(false, false, true), 0);
        if (prefix.StartsWith(new byte[] { 0, 0, 0, 0x3C })) return (new UTF32Encoding(true, false, true), 0);
        if (prefix.StartsWith(new byte[] { 0x3C, 0 })) return (new UnicodeEncoding(false, false, true), 0);
        if (prefix.StartsWith(new byte[] { 0, 0x3C })) return (new UnicodeEncoding(true, false, true), 0);
        // BOM/signature takes precedence, including previously supported mismatched declarations.
        if (!prefix.StartsWith("<?xml"u8) || bytes.Length <= 5 ||
            bytes[5] is not (0x20 or 0x09 or 0x0D or 0x0A))
            return (new UTF8Encoding(false, true), 0);
        // The declaration is ASCII in all remaining supported encodings. Scan its
        // actual terminator within the input budgets, never an arbitrary prefix.
        int end = 5;
        while (end + 1 < bytes.Length && !(bytes[end] == '?' && bytes[end + 1] == '>'))
        {
            if ((end & 4095) == 0) token.ThrowIfCancellationRequested();
            if (end >= options.MaxXmlCharacters) throw new DocumentBudgetExceededException("XmlCharacters");
            end++;
        }
        token.ThrowIfCancellationRequested();
        if (end + 1 >= bytes.Length) throw new XmlException("XML 声明未结束。");
        if ((long)end + 2 > options.MaxXmlCharacters) throw new DocumentBudgetExceededException("XmlCharacters");
        string declaration = Encoding.ASCII.GetString(bytes, 0, end + 2);
        var match = Regex.Match(declaration, "^<\\?xml\\s+[^?]*encoding\\s*=\\s*['\"]([^'\"]+)['\"]",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        if (!match.Success) return (new UTF8Encoding(false, true), 0);
        try { return (Encoding.GetEncoding(match.Groups[1].Value, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), 0); }
        catch (ArgumentException) { throw new XmlException("不支持 XML 声明的编码。"); }
    }

    public static XDocument LoadSafe(TextReader textReader)
    {
        ArgumentNullException.ThrowIfNull(textReader);
        var options = new DocumentReadOptions();
        var text = new StringBuilder();
        char[] buffer = new char[4096];
        int read;
        while ((read = textReader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if ((long)text.Length + read > options.MaxXmlCharacters) throw new DocumentBudgetExceededException("XmlCharacters");
            text.Append(buffer, 0, read);
        }
        return ParseSafe(text.ToString(), options);
    }

    public static XDocument ParseSafe(string xml) => ParseSafe(xml, new DocumentReadOptions());

    public static XDocument ParseSafe(string xml, DocumentReadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xml);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (xml.Length > options.MaxXmlCharacters) throw new DocumentBudgetExceededException("XmlCharacters");
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = options.MaxXmlCharacters
        });
        using var bounded = new BudgetXmlReader(reader, options, cancellationToken);
        return XDocument.Load(bounded, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
    }

    private sealed class BudgetXmlReader(XmlReader inner, DocumentReadOptions options, CancellationToken token)
        : XmlReader, IXmlLineInfo
    {
        private long _nodes;
        public override bool Read()
        {
            token.ThrowIfCancellationRequested();
            bool read = inner.Read();
            if (read)
            {
                if (inner.Depth + 1 > options.MaxDepth) throw new DocumentBudgetExceededException("Depth");
                _nodes += 1L + inner.AttributeCount;
                if (_nodes > options.MaxNodes) throw new DocumentBudgetExceededException("Nodes");
            }
            return read;
        }
        public bool HasLineInfo() => (inner as IXmlLineInfo)?.HasLineInfo() == true;
        public int LineNumber => (inner as IXmlLineInfo)?.LineNumber ?? 0;
        public int LinePosition => (inner as IXmlLineInfo)?.LinePosition ?? 0;
        public override int AttributeCount => inner.AttributeCount;
        public override string BaseURI => inner.BaseURI;
        public override int Depth => inner.Depth;
        public override bool EOF => inner.EOF;
        public override bool IsEmptyElement => inner.IsEmptyElement;
        public override string LocalName => inner.LocalName;
        public override string NamespaceURI => inner.NamespaceURI;
        public override XmlNameTable NameTable => inner.NameTable;
        public override XmlNodeType NodeType => inner.NodeType;
        public override string Prefix => inner.Prefix;
        public override ReadState ReadState => inner.ReadState;
        public override string Value => inner.Value;
        public override string GetAttribute(int i) => inner.GetAttribute(i);
        public override string? GetAttribute(string name) => inner.GetAttribute(name);
        public override string? GetAttribute(string name, string? namespaceURI) => inner.GetAttribute(name, namespaceURI);
        public override string? LookupNamespace(string prefix) => inner.LookupNamespace(prefix);
        public override bool MoveToAttribute(string name) => inner.MoveToAttribute(name);
        public override bool MoveToAttribute(string name, string? ns) => inner.MoveToAttribute(name, ns);
        public override void MoveToAttribute(int i) => inner.MoveToAttribute(i);
        public override bool MoveToElement() => inner.MoveToElement();
        public override bool MoveToFirstAttribute() => inner.MoveToFirstAttribute();
        public override bool MoveToNextAttribute() => inner.MoveToNextAttribute();
        public override bool ReadAttributeValue() => inner.ReadAttributeValue();
        public override void ResolveEntity() => inner.ResolveEntity();
    }
}
