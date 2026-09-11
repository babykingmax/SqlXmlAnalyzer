using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Tests;

public sealed class AcceptanceProbeBoundaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP29-boundary-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, false)]
    public void ExitCode_ControlsPersistedAcceptanceResult(int code, bool passed)
    {
        AcceptanceProbeBoundary.Execute(() => code, _directory).Should().Be(code);
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "boundary.json")));
        result.RootElement.GetProperty("Passed").GetBoolean().Should().Be(passed);
        result.RootElement.GetProperty("ExitCode").GetInt32().Should().Be(code);
    }

    [Fact]
    public void UnknownFailure_WritesRealDumpAndCannotBeReportedAsSuccess()
    {
        var error = new InvalidOperationException("IMP29 synthetic failure");
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), Path.Combine(_directory, "dumps"));
        AcceptanceProbeBoundary.Execute(() =>
        {
            AcceptanceProbeBoundary.Describe(error);
            return 0; // A swallowed exception in a reused probe must still fail the outer boundary.
        }, _directory, reporter).Should().Be(1);
        var diagnostic = reporter.Report(error, "verify");
        diagnostic.DumpCreated.Should().BeTrue(diagnostic.Summary);
        MinidumpValidator.Validate(diagnostic.DumpPath!);
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "boundary.json")));
        result.RootElement.GetProperty("Passed").GetBoolean().Should().BeFalse();
        string log = File.ReadAllText(Path.Combine(_directory, "probe.log"));
        log.Should().Contain("[CRITICAL]");
    }

    [Fact]
    public void ExpectedCancellationAndFailure_RespectBuildLoggingGateAndDoNotCreateDump()
    {
        AcceptanceProbeBoundary.Execute(() =>
        {
            AcceptanceProbeBoundary.Describe(new OperationCanceledException());
            throw new IOException("IMP29 synthetic write failure");
        }, _directory).Should().Be(1);
        string log = File.ReadAllText(Path.Combine(_directory, "probe.log"));
        log.Should().Contain("[ERROR]").And.NotContain("[CRITICAL]");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]").And.NotContain("[VERBOSE]");
#endif
        Directory.Exists(Path.Combine(_directory, "dumps")).Should().BeFalse();
    }

    public void Dispose() { Logger.Shutdown(); if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
