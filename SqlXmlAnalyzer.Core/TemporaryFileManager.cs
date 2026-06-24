using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SqlXmlAnalyzer.Core
{
    public sealed class TemporaryFileManager : IDisposable
    {
        public const string FilePrefix = "SqlXmlAnalyzer_";

        private readonly string _temporaryDirectory;
        private readonly ConcurrentDictionary<string, byte> _sessionFiles =
            new(StringComparer.OrdinalIgnoreCase);

        public TemporaryFileManager(string? temporaryDirectory = null)
        {
            _temporaryDirectory = Path.GetFullPath(temporaryDirectory ?? Path.GetTempPath());
            Directory.CreateDirectory(_temporaryDirectory);
        }

        public string CreatePath(string purpose, string extension)
        {
            string safePurpose = new string((purpose ?? "Temp")
                .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
                .ToArray());
            if (safePurpose.Length == 0)
            {
                safePurpose = "Temp";
            }

            string normalizedExtension = string.IsNullOrWhiteSpace(extension)
                ? ".tmp"
                : extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
            string path = Path.Combine(
                _temporaryDirectory,
                $"{FilePrefix}{safePurpose}_{Guid.NewGuid():N}{normalizedExtension}");
            _sessionFiles.TryAdd(path, 0);
            return path;
        }

        public bool Delete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsManagedPath(path))
            {
                return false;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                _sessionFiles.TryRemove(Path.GetFullPath(path), out _);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"临时文件清理失败: {path} | {ex.Message}");
                return false;
            }
        }

        public int CleanupStaleFiles(TimeSpan maximumAge)
        {
            int deleted = 0;
            DateTime cutoff = DateTime.UtcNow - maximumAge;

            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(
                    _temporaryDirectory,
                    $"{FilePrefix}*",
                    SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                Logger.Warning($"无法枚举历史临时文件: {ex.Message}");
                return 0;
            }

            foreach (string path in candidates)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff && Delete(path))
                    {
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"检查临时文件失败: {path} | {ex.Message}");
                }
            }

            return deleted;
        }

        public void CleanupSessionFiles()
        {
            foreach (string path in _sessionFiles.Keys.ToArray())
            {
                Delete(path);
            }
        }

        public bool IsManagedPath(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return false;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            return string.Equals(directory, _temporaryDirectory, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(fullPath).StartsWith(FilePrefix, StringComparison.Ordinal);
        }

        public void Dispose()
        {
            CleanupSessionFiles();
        }
    }
}
