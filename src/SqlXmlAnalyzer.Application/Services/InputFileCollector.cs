using System.Text.RegularExpressions;

namespace SqlXmlAnalyzer.Application.Services;

public static class InputFileCollector
{
    private static readonly string[] DefaultExclusions = [".git", "bin", "obj", ".vs", "publish-*", "backups", ".tmp.*"];

    public static IReadOnlyList<string> Collect(string rootPath, IEnumerable<string> extensions,
        IEnumerable<string>? additionalExclusions = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(rootPath)) return [];
        var allowed = extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exclusions = DefaultExclusions.Concat(additionalExclusions ?? [])
            .Select(pattern => pattern.Trim()).Where(pattern => pattern.Length != 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(pattern => new Regex("\\A" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "\\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).ToArray();
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootPath));
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (allowed.Contains(Path.GetExtension(file))) files.Add(file);
                }
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (exclusions.Any(pattern => pattern.IsMatch(Path.GetFileName(child)))) continue;
                    // Junctions and symbolic links can point back to ancestors or outside the requested tree.
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(child);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[Warning] Skipping unreadable directory '{directory}': {exception.Message}");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }
}
