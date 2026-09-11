using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Release;

public sealed record ReleaseFile(string Path, long Bytes, string Sha256);
public sealed record ReleaseManifest(int SchemaVersion, string Id, string Kind, DateTime CreatedUtc,
    bool ApprovedForDistribution, IReadOnlyList<ReleaseFile> Files);

/// <summary>Offline integrity checks and recovery to a new directory. Hashes are not signatures.</summary>
public static class ReleaseBundle
{
    public const string ManifestName = "bundle-manifest.json";
    private const int MaxFiles = 10000;
    private const long MaxBytes = 8L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Seal(string directory, string id, string kind)
    {
        if (!Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,100}\z") || kind is not ("candidate" or "recovery"))
            throw new InvalidDataException("RELEASE_METADATA_INVALID");
        string root = Path.GetFullPath(directory);
        string manifest = Path.Combine(root, ManifestName);
        if (File.Exists(manifest)) throw new IOException("RELEASE_MANIFEST_EXISTS");
        var files = Inventory(root, false);
        if (files.Count == 0) throw new InvalidDataException("RELEASE_BUNDLE_EMPTY");
        using (var stream = new FileStream(manifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new ReleaseManifest(1, id, kind, DateTime.UtcNow, false, files), Json);
            stream.Flush(true);
        }
        string hash = Hash(manifest);
        Verify(root, hash);
        Logger.Debug($"Release bundle sealed; files={files.Count}.");
        return hash;
    }

    public static ReleaseManifest Verify(string directory, string expectedManifestSha256)
    {
        string root = Path.GetFullPath(directory);
        CheckAncestors(root);
        string path = Path.Combine(root, ManifestName);
        CheckAncestors(path);
        RequireHash(expectedManifestSha256);
        ReleaseManifest manifest;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > 4 * 1024 * 1024 || !Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RELEASE_MANIFEST_HASH_MISMATCH");
            stream.Position = 0;
            try { manifest = JsonSerializer.Deserialize<ReleaseManifest>(stream) ?? throw new InvalidDataException("RELEASE_MANIFEST_INVALID"); }
            catch (JsonException exception) { throw new InvalidDataException("RELEASE_MANIFEST_INVALID", exception); }
        }
        if (manifest.SchemaVersion != 1 || manifest.ApprovedForDistribution || string.IsNullOrWhiteSpace(manifest.Id)
            || manifest.Kind is not ("candidate" or "recovery") || manifest.Files == null || manifest.Files.Count is 0 or > MaxFiles)
            throw new InvalidDataException("RELEASE_MANIFEST_UNSUPPORTED");
        var expected = new Dictionary<string, ReleaseFile>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var file in manifest.Files)
        {
            if (file == null) throw new InvalidDataException("RELEASE_ENTRY_INVALID");
            ValidateRelativePath(file.Path);
            RequireHash(file.Sha256);
            if (file.Bytes < 0 || file.Bytes > MaxBytes - bytes || !expected.TryAdd(file.Path, file))
                throw new InvalidDataException("RELEASE_ENTRY_INVALID");
            bytes += file.Bytes;
        }
        var actual = Inventory(root, true);
        if (actual.Count != expected.Count) throw new InvalidDataException("RELEASE_FILE_SET_MISMATCH");
        foreach (var file in actual)
            if (!expected.TryGetValue(file.Path, out var record) || record.Bytes != file.Bytes
                || !record.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RELEASE_FILE_HASH_MISMATCH");
        Logger.Debug($"Release bundle verified; files={actual.Count}.");
        return manifest;
    }

    // Never replaces an installation or a live SQL file. The caller selects restored copies explicitly.
    public static void Restore(string directory, string expectedManifestSha256, string destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string source = Path.GetFullPath(directory);
        var manifest = Verify(source, expectedManifestSha256);
        string target = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        if (target.Equals(source, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("RELEASE_DESTINATION_NESTED");
        CheckAncestors(target);
        if (Path.Exists(target)) throw new IOException("RELEASE_DESTINATION_EXISTS");
        string parent = Path.GetDirectoryName(target) ?? throw new InvalidDataException("RELEASE_DESTINATION_INVALID");
        string stage = Path.Combine(parent, ".restore-" + Guid.NewGuid().ToString("N"));
        DiagnosticFileAccess.CreatePrivateDirectory(stage);
        try
        {
            foreach (var file in manifest.Files.Append(new ReleaseFile(ManifestName,
                new FileInfo(Path.Combine(source, ManifestName)).Length, expectedManifestSha256)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string from = Path.Combine(source, file.Path);
                CheckAncestors(from);
                string to = Path.Combine(stage, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                if (input.Length != file.Bytes) throw new InvalidDataException("RELEASE_SOURCE_CHANGED");
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    total += count;
                    if (total > file.Bytes) throw new InvalidDataException("RELEASE_SOURCE_CHANGED");
                    output.Write(buffer, 0, count);
                }
                output.Flush(true);
            }
            Verify(stage, expectedManifestSha256);
            Verify(source, expectedManifestSha256);
            cancellationToken.ThrowIfCancellationRequested();
            CheckAncestors(target);
            Directory.Move(stage, target); // Atomic directory rename on the same volume; no overwrite.
            Logger.Debug("Release recovery committed to a new directory.");
        }
        catch
        {
            // Keep partial recovery private for diagnosis. Never delete caller data on a failure path.
            Logger.Warning("Release recovery failed before commit; private staging directory retained.");
            throw;
        }
    }

    public static string Hash(string path)
    {
        CheckAncestors(Path.GetFullPath(path));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static List<ReleaseFile> Inventory(string root, bool hasManifest)
    {
        CheckAncestors(root);
        var files = new List<ReleaseFile>();
        var directories = new Stack<string>();
        directories.Push(root);
        long bytes = 0;
        int entries = 0;
        while (directories.TryPop(out var directory))
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > MaxFiles * 2) throw new InvalidDataException("RELEASE_ENTRY_BUDGET");
                CheckAncestors(path);
                if (Directory.Exists(path)) { directories.Push(path); continue; }
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (hasManifest && relative.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
                ValidateRelativePath(relative);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                long length = stream.Length;
                if (files.Count >= MaxFiles || length > MaxBytes - bytes) throw new InvalidDataException("RELEASE_SIZE_BUDGET");
                bytes += length;
                files.Add(new(relative, length, Convert.ToHexString(SHA256.HashData(stream))));
            }
        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
    }

    private static void RequireHash(string value)
    {
        if (value == null || !Regex.IsMatch(value, @"\A[0-9A-Fa-f]{64}\z"))
            throw new InvalidDataException("RELEASE_HASH_INVALID");
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)
            || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path))
            throw new InvalidDataException("RELEASE_PATH_INVALID");
        foreach (string part in path.Split('/'))
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || Regex.IsMatch(part, @"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|\z)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("RELEASE_PATH_INVALID");
    }

    private static void CheckAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("RELEASE_REPARSE_POINT");
    }
}
