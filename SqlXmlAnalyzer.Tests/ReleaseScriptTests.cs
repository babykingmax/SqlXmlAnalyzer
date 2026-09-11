using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace SqlXmlAnalyzer.Tests;

public sealed class ReleaseScriptTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("timeout")]
    [InlineData("boundary")]
    [InlineData("git-visible")]
    [InlineData("restore-entrypoint")]
    [InlineData("pipe-timeout")]
    [InlineData("relative-paths")]
    [InlineData("preserve-evidence")]
    [InlineData("candidate-match")]
    [InlineData("candidate-stale-build")]
    [InlineData("candidate-embedded-build")]
    [InlineData("candidate-status-build")]
    [InlineData("candidate-kind")]
    [InlineData("candidate-id")]
    [InlineData("candidate-missing-status")]
    [InlineData("expected-validation")]
    [InlineData("expected-json")]
    [InlineData("expected-missing-file")]
    [InlineData("boundary-unknown-string")]
    [InlineData("capture-read-modified")]
    [InlineData("capture-error-modified")]
    [InlineData("capture-read-missing")]
    public async Task Script_PreservesExitCodesArgumentsAndDiagnostics(string scenario)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "SqlXmlAnalyzer.sln"))) root = root.Parent;
        root.Should().NotBeNull();
        string output = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP30-script-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(root!.FullName, "DOCS/verification/IMP-30/Test-Release.ps1"), "-Case", scenario, "-OutputDirectory", output, "-CoreAssembly", typeof(Logger).Assembly.Location }) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException("Release script timed out: " + scenario); }
            string detail = await stdout + Environment.NewLine + await stderr;
            process.ExitCode.Should().Be(0, detail);
            detail.Should().Contain("PASS: " + scenario);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }
}
