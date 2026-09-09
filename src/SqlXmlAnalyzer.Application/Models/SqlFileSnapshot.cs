using System.Security.Cryptography;
using System.Text;

namespace SqlXmlAnalyzer.Application.Models;

public sealed class SqlFileSnapshot
{
    private readonly byte[] _bytes;
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;

    public string Path { get; }
    public string Text { get; }
    public string Sha256 { get; }
    public int ByteLength => _bytes.Length;

    private SqlFileSnapshot(string path, byte[] bytes, Encoding encoding, int preambleLength)
    {
        Path = System.IO.Path.GetFullPath(path);
        _bytes = (byte[])bytes.Clone();
        _encoding = encoding;
        _preamble = bytes[..preambleLength];
        Text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        Sha256 = Hash(bytes);
    }

    public static SqlFileSnapshot FromBytes(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        // Check UTF-32 before UTF-16 because their little-endian BOM prefixes overlap.
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }))
            return new(path, bytes, new UTF32Encoding(false, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
            return new(path, bytes, new UTF32Encoding(true, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return new(path, bytes, new UTF8Encoding(true, true), 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            return new(path, bytes, new UnicodeEncoding(false, true, true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            return new(path, bytes, new UnicodeEncoding(true, true, true), 2);
        // Do not silently replace undecodable bytes in a file that may be written back.
        return new(path, bytes, new UTF8Encoding(false, true), 0);
    }

    public byte[] CopyOriginalBytes() => (byte[])_bytes.Clone();

    public byte[] Encode(string text)
    {
        byte[] content = _encoding.GetBytes(text);
        byte[] output = new byte[_preamble.Length + content.Length];
        _preamble.CopyTo(output, 0);
        content.CopyTo(output, _preamble.Length);
        return output;
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
