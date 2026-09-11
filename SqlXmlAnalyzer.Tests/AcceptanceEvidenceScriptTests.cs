using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace SqlXmlAnalyzer.Tests;

public sealed class AcceptanceEvidenceScriptTests
{
    [Theory]
    [InlineData("unchanged")]
    [InlineData("source-changed")]
    [InlineData("source-added")]
    [InlineData("source-deleted")]
    [InlineData("binary-changed")]
    [InlineData("build-result-changed")]
    [InlineData("build-changed-during-stage")]
    [InlineData("source-changed-during-stage")]
    [InlineData("stage-result-changed")]
    [InlineData("stage-different-build")]
    [InlineData("stage-unbound-result")]
    [InlineData("duplicate-records")]
    [InlineData("path-escape")]
    [InlineData("preserve-existing-evidence")]
    [InlineData("git-evidence-visible")]
    [InlineData("warnings-0-en-US")]
    [InlineData("warnings-1-en-US")]
    [InlineData("warnings-10-en-US")]
    [InlineData("warnings-20-en-US")]
    [InlineData("warnings-0-zh-CN")]
    [InlineData("warnings-1-zh-CN")]
    [InlineData("warnings-10-zh-CN")]
    [InlineData("warnings-20-zh-CN")]
    [InlineData("warnings-incremental")]
    [InlineData("dump-load-context")]
    public async Task EvidenceGuard_RejectsStaleOrUnpublishableAcceptance(string scenario)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "SqlXmlAnalyzer.sln"))) root = root.Parent;
        root.Should().NotBeNull("acceptance script regressions run from a source checkout");
        string output = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP29-evidence-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root!.FullName
        };
        foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(root.FullName, "DOCS/verification/IMP-29/Test-Evidence.ps1"), "-Case", scenario, "-OutputDirectory", output })
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw new TimeoutException("Acceptance evidence regression timed out: " + scenario); }
            string detail = await stdout + Environment.NewLine + await stderr;
            process.ExitCode.Should().Be(0, detail);
            detail.Should().Contain("PASS: " + scenario);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }
}
