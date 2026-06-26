using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.CLI;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Application
{
    public sealed class CliScanFileCollectionTests : IDisposable
    {
        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer_Cli_{Guid.NewGuid():N}");

        public CliScanFileCollectionTests()
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
        public void CollectPlanFiles_SkipsDefaultGeneratedDirectories()
        {
            string includedFile = WritePlanFile("plans", "include.sqlplan");
            WritePlanFile("bin", "skip-bin.sqlplan");
            WritePlanFile("obj", "skip-obj.sqlplan");
            WritePlanFile(".vs", "skip-vs.sqlplan");
            WritePlanFile("publish-win-x64", "skip-publish.sqlplan");
            WritePlanFile("backups", "skip-backups.sqlplan");
            WritePlanFile(".tmp.scan", "skip-temp.sqlplan");

            IReadOnlyList<string> files = Program.CollectPlanFiles(_tempDirectory);

            files.Should().ContainSingle().Which.Should().Be(includedFile);
        }

        [Fact]
        public void CollectPlanFiles_AppliesAdditionalExcludePatterns()
        {
            string includedFile = WritePlanFile("plans", "include.sqlplan");
            WritePlanFile("scratch", "skip-custom.sqlplan");

            IReadOnlyList<string> files = Program.CollectPlanFiles(
                _tempDirectory,
                new[] { "scratch" });

            files.Should().ContainSingle().Which.Should().Be(includedFile);
        }

        private string WritePlanFile(string relativeDirectory, string fileName)
        {
            string directory = Path.Combine(_tempDirectory, relativeDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, "<ShowPlanXML />");
            return path;
        }
    }
}
