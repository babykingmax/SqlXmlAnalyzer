using System;
using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class TemporaryFileManagerTests : IDisposable
    {
        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer_TempTests_{Guid.NewGuid():N}");

        public TemporaryFileManagerTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public void CreatePath_UsesManagedPrefixAndUniqueNames()
        {
            using var manager = new TemporaryFileManager(_tempDirectory);

            string first = manager.CreatePath("Mermaid", ".html");
            string second = manager.CreatePath("Mermaid", ".html");

            Path.GetFileName(first).Should().StartWith(TemporaryFileManager.FilePrefix);
            Path.GetExtension(first).Should().Be(".html");
            second.Should().NotBe(first);
            manager.IsManagedPath(first).Should().BeTrue();
        }

        [Fact]
        public void Delete_OnlyDeletesManagedFiles()
        {
            using var manager = new TemporaryFileManager(_tempDirectory);
            string managed = manager.CreatePath("Graph", ".png");
            string unrelated = Path.Combine(_tempDirectory, "unrelated.png");
            File.WriteAllText(managed, "managed");
            File.WriteAllText(unrelated, "unrelated");

            manager.Delete(managed).Should().BeTrue();
            manager.Delete(unrelated).Should().BeFalse();

            File.Exists(managed).Should().BeFalse();
            File.Exists(unrelated).Should().BeTrue();
        }

        [Fact]
        public void CleanupStaleFiles_DeletesOnlyOldPrefixedFiles()
        {
            using var manager = new TemporaryFileManager(_tempDirectory);
            string oldManaged = manager.CreatePath("Old", ".tmp");
            string freshManaged = manager.CreatePath("Fresh", ".tmp");
            string unrelated = Path.Combine(_tempDirectory, "old-unrelated.tmp");
            File.WriteAllText(oldManaged, "old");
            File.WriteAllText(freshManaged, "fresh");
            File.WriteAllText(unrelated, "unrelated");
            File.SetLastWriteTimeUtc(oldManaged, DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-2));

            int deleted = manager.CleanupStaleFiles(TimeSpan.FromHours(24));

            deleted.Should().Be(1);
            File.Exists(oldManaged).Should().BeFalse();
            File.Exists(freshManaged).Should().BeTrue();
            File.Exists(unrelated).Should().BeTrue();
        }

        [Fact]
        public void Dispose_DeletesTrackedSessionFiles()
        {
            string path;
            using (var manager = new TemporaryFileManager(_tempDirectory))
            {
                path = manager.CreatePath("Session", ".tmp");
                File.WriteAllText(path, "session");
            }

            File.Exists(path).Should().BeFalse();
        }
    }
}
