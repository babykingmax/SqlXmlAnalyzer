using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SqlXmlAnalyzer.Core.Diagnostics;

public interface IDiagnosticPackageWriter
{
    void Write(DiagnosticPackage package, string path, CancellationToken token);
}

public sealed class DiagnosticPackageZipWriter : IDiagnosticPackageWriter
{
    public void Write(DiagnosticPackage package, string path, CancellationToken token)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in package.Entries)
            {
                token.ThrowIfCancellationRequested();
                using var stream = archive.CreateEntry(entry.Name, CompressionLevel.Fastest).Open();
                entry.WriteTo(stream, token);
            }
        file.Flush(flushToDisk: true);
    }
}

public sealed class DiagnosticPackageExporter(IDiagnosticPackageWriter? writer = null, IUnexpectedErrorReporter? unexpectedErrors = null)
{
    public DiagnosticPackageSaveResult Save(DiagnosticPackage package, string path, CancellationToken token = default)
    {
        string? stagingDirectory = null;
        string? temporary = null;
        try
        {
            token.ThrowIfCancellationRequested();
            string destination;
            try
            {
                if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("诊断数据包必须保存为 .zip。");
                destination = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            { throw new InvalidDataException("诊断包保存路径无效。", exception); }
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("请使用新的文件名；诊断包不会覆盖既有文件。");
            string parent = Path.GetDirectoryName(destination)!;
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("保存目录不存在。");
            stagingDirectory = Path.Combine(parent, ".tmp.diagnostic-" + Guid.NewGuid().ToString("N"));
            DiagnosticFileAccess.CreatePrivateDirectory(stagingDirectory);
            temporary = Path.Combine(stagingDirectory, "package.zip");
            Logger.Debug("IMP27.Package.Save started.");
            (writer ?? new DiagnosticPackageZipWriter()).Write(package, temporary, token);
            DiagnosticPackageValidator.Validate(temporary, token);
            // Also compare against the reviewed bytes: a valid but substituted ZIP is not the reviewed package.
            using (var file = File.OpenRead(temporary))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
            {
                if (zip.Entries.Count != package.Entries.Count) throw new InvalidDataException("保存内容与审核快照不一致。");
                foreach (var entry in package.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    var actual = zip.GetEntry(entry.Name);
                    if (actual == null || actual.Length != entry.Bytes) throw new InvalidDataException("保存文件与审核清单不一致。");
                    using var stream = actual.Open();
                    if (DiagnosticPackageValidator.Hash(stream, token, entry.Bytes) != entry.Sha256) throw new InvalidDataException("保存内容校验失败。");
                }
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            temporary = null;
            Logger.Debug("IMP27.Package.Save completed.");
            return new(true, "诊断数据包已保存为本地新文件，内容与预览一致，SHA-256 校验通过。" + package.PrivacyNotice);
        }
        catch (Exception exception)
        {
            return new(false, ExceptionPolicy.Describe(exception, "IMP27.Package.Save", unexpectedErrors));
        }
        finally
        {
            try
            {
                if (temporary != null) File.Delete(temporary);
                if (stagingDirectory != null && Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: false);
            }
            catch (Exception exception) { ExceptionPolicy.Describe(exception, "IMP27.Package.Cleanup", unexpectedErrors); }
        }
    }
}

/// <summary>Checks a local ZIP without extracting entries or trusting their paths. SHA-256 is integrity, not a signature.</summary>
public static class DiagnosticPackageValidator
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "context.json", "configuration.json", "errors.json", "missing-evidence.json", "report.json", "redaction.json",
        "selection.xml", "application.log", "process.dmp", "manifest.json", "SHA256SUMS"
    };
    public static void Validate(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > DiagnosticPackage.MaxPackageBytes + 1024 * 1024) throw new InvalidDataException("ZIP 文件超过预算。");
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            if (archive.Entries.Count > Names.Count || archive.Entries.Count < 6
                || archive.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count
                || archive.Entries.Any(e => !Names.Contains(e.FullName) || e.Length > DiagnosticPackage.MaxEntryBytes)
                || archive.Entries.Sum(e => e.Length) > DiagnosticPackage.MaxPackageBytes)
                throw new InvalidDataException("诊断包包含重复、非法路径或超预算内容。");
            foreach (string required in new[] { "context.json", "configuration.json", "errors.json", "missing-evidence.json", "manifest.json", "SHA256SUMS" })
                if (archive.GetEntry(required) == null) throw new InvalidDataException("诊断包缺少必需文件。");
            var sums = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in ReadText(archive.GetEntry("SHA256SUMS")!, 16 * 1024, token).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length < 67 || line[64..66] != "  " || line[..64].Any(c => !char.IsAsciiHexDigit(c))
                    || !sums.TryAdd(line[66..], line[..64])) throw new InvalidDataException("SHA256SUMS 格式无效。");
            }
            if (sums.Count != archive.Entries.Count - 1 || sums.ContainsKey("SHA256SUMS")) throw new InvalidDataException("校验清单与实际文件数量不一致。");
            foreach (var entry in archive.Entries.Where(e => e.FullName != "SHA256SUMS"))
            {
                token.ThrowIfCancellationRequested();
                using var stream = entry.Open();
                if (!sums.TryGetValue(entry.FullName, out var expected) || Hash(stream, token, entry.Length) != expected)
                    throw new InvalidDataException("诊断包 SHA-256 校验失败。");
            }
            using var manifest = JsonDocument.Parse(ReadText(archive.GetEntry("manifest.json")!, 1024 * 1024, token));
            var root = manifest.RootElement;
            if (root.GetProperty("SchemaVersion").GetString() != DiagnosticPackage.CurrentVersion || root.GetProperty("HashAlgorithm").GetString() != "SHA-256")
                throw new InvalidDataException("不支持此诊断包版本或校验算法。");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in root.GetProperty("Files").EnumerateArray())
            {
                string name = item.GetProperty("Name").GetString()!;
                var entry = archive.GetEntry(name);
                if (!seen.Add(name) || name is "manifest.json" or "SHA256SUMS" || entry == null
                    || entry.Length != item.GetProperty("Bytes").GetInt64() || sums[name] != item.GetProperty("Sha256").GetString())
                    throw new InvalidDataException("manifest 与实际文件不一致。");
            }
            if (seen.Count != archive.Entries.Count - 2) throw new InvalidDataException("manifest 未列出所有实际内容。");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        { throw new InvalidDataException("诊断包清单无效。", exception); }
    }
    private static string ReadText(ZipArchiveEntry entry, int maxBytes, CancellationToken token)
    {
        if (entry.Length > maxBytes) throw new InvalidDataException("诊断包清单超过预算。");
        using var stream = entry.Open();
        var bytes = new byte[(int)entry.Length];
        for (int offset = 0; offset < bytes.Length;)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
            if (read == 0) throw new InvalidDataException("ZIP 清单长度无效。");
            offset += read;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("ZIP 清单解压数据超过声明长度。");
        return new UTF8Encoding(false, true).GetString(bytes);
    }
    internal static string Hash(Stream stream, CancellationToken token, long expectedLength)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.Read(buffer);
            if (read == 0) break;
            total += read;
            if (total > DiagnosticPackage.MaxEntryBytes || total > expectedLength) throw new InvalidDataException("ZIP 条目解压数据超过预算或声明长度。");
            hash.AppendData(buffer, 0, read);
        }
        if (total != expectedLength) throw new InvalidDataException("ZIP 条目长度无效。");
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
