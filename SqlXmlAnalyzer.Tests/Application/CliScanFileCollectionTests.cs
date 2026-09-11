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
            string full = Path.GetFullPath(_tempDirectory);
            if (!full.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer_Cli_"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected cleanup path");
            if (Directory.Exists(full))
            {
                Directory.Delete(full, true);
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

        [Fact]
        public void CollectPlanFiles_SkipsAncestorJunctionAndDoesNotDuplicatePlans()
        {
            string included = WritePlanFile("plans", "include.sqlplan");
            string junction = Path.Combine(_tempDirectory, "plans", "loop");
            // NTFS directory junctions do not require the symbolic-link privilege.
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in new[] { "/c", "mklink", "/J", junction, _tempDirectory }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            process.WaitForExit(10000).Should().BeTrue();
            process.ExitCode.Should().Be(0, process.StandardError.ReadToEnd());
            try
            {
                Program.CollectPlanFiles(_tempDirectory).Should().ContainSingle().Which.Should().Be(included);
            }
            finally { Directory.Delete(junction); }
        }

        [Fact]
        public void CollectPlanFiles_CancellationWhilePreparingTraversalStopsCollection()
        {
            WritePlanFile("plans", "include.sqlplan");
            using var cancellation = new System.Threading.CancellationTokenSource();
            IEnumerable<string> Exclusions()
            {
                yield return "unused";
                cancellation.Cancel();
            }
            Action collect = () => Program.CollectPlanFiles(_tempDirectory, Exclusions(), cancellation.Token);
            collect.Should().Throw<OperationCanceledException>();
        }

        [Fact]
        public void CollectPlanFiles_AlreadyCanceledMissingRootDoesNotReportSuccessfulEmptyScan()
        {
            Action collect = () => Program.CollectPlanFiles(Path.Combine(_tempDirectory, "missing"),
                cancellationToken: new System.Threading.CancellationToken(true));
            collect.Should().Throw<OperationCanceledException>();
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
