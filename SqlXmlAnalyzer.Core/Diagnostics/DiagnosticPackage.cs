using System.Security.Cryptography;
using System.Text;

namespace SqlXmlAnalyzer.Core.Diagnostics;

public sealed record DiagnosticPackageOptions(bool IncludeReport = true, bool IncludeRawSelection = false,
    string? LogPath = null, string? DumpPath = null);

/// <summary>Owns the exact bytes shown in review and subsequently saved; never reopens an input on export.</summary>
public sealed class DiagnosticPackageEntry
{
    private readonly byte[] _bytes;
    internal DiagnosticPackageEntry(string name, byte[] bytes, string privacy, bool binary = false)
    {
        Name = name; _bytes = bytes; Privacy = privacy; IsBinary = binary;
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
    }
    public string Name { get; }
    public long Bytes => _bytes.LongLength;
    public string Sha256 { get; }
    public string Privacy { get; }
    public string PrivacyLabel => Privacy switch { "RawSensitive" => "未脱敏 · 敏感", "Redacted" => "已脱敏 · 保留结构", _ => "已筛选元数据" };
    public bool IsBinary { get; }
    public string Preview => IsBinary
        ? $"二进制 DUMP，未脱敏；本界面不能检查内存中的敏感内容。\n字节数：{Bytes}\nSHA-256：{Sha256}\n请使用本地调试器检查；不能据此认定可公开分享。"
        : Encoding.UTF8.GetString(_bytes);
    internal void WriteTo(Stream stream, CancellationToken token)
    {
        for (int offset = 0; offset < _bytes.Length; offset += 64 * 1024)
        {
            token.ThrowIfCancellationRequested();
            stream.Write(_bytes, offset, Math.Min(64 * 1024, _bytes.Length - offset));
        }
    }
}

public sealed class DiagnosticPackage
{
    public const string CurrentVersion = "IMP27-1.0";
    public const long MaxEntryBytes = 64L * 1024 * 1024;
    public const long MaxPackageBytes = 128L * 1024 * 1024;
    public const int MaxLogBytes = 2 * 1024 * 1024;
    internal DiagnosticPackage(IEnumerable<DiagnosticPackageEntry> entries) => Entries = Array.AsReadOnly(entries.ToArray());
    public IReadOnlyList<DiagnosticPackageEntry> Entries { get; }
    public long TotalBytes => Entries.Sum(e => e.Bytes);
    public bool ContainsSensitiveContent => Entries.Any(e => e.Privacy == "RawSensitive");
    public string PrivacyNotice => ContainsSensitiveContent
        ? "包含明确选择的未脱敏内容，可能含 SQL、标识符、路径、凭据或内存数据；请逐项审核。仅保存本地，不自动发送。"
        : "默认包不包含原始输入、日志或 DUMP；脱敏仍保留结构和数值，不保证匿名。仅保存本地，不自动发送。";
}

public sealed record DiagnosticPackagePreparation(DiagnosticPackage? Package, string Message)
{
    public bool Success => Package != null;
}
public sealed record DiagnosticPackageSaveResult(bool Success, string Message);
