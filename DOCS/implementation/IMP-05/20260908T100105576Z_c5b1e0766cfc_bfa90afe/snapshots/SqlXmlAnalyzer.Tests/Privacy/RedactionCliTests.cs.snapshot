using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.CLI;

namespace SqlXmlAnalyzer.Tests.Privacy;

public sealed class RedactionCliTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-Imp05Cli-" + Guid.NewGuid().ToString("N"));
    public RedactionCliTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RedactCommand_ExportsOnlySupportedPlanAndKeepsInput(bool unsupported)
    {
        var plan = PlanRedactionServiceTests.Fixture();
        if (unsupported) plan.Root!.SetAttributeValue("Unknown", PlanRedactionServiceTests.Marker);
        string input = Path.Combine(_directory, "input.sqlplan"), output = Path.Combine(_directory, "redacted.sqlplan");
        File.WriteAllText(input, plan.ToString());
        byte[] before = File.ReadAllBytes(input);
        var old = Console.Out; var capture = new StringWriter();
        int code;
        try { Console.SetOut(capture); code = Program.Main(new[] { "redact", input, "--output", output }); }
        finally { Console.SetOut(old); }
        code.Should().Be(unsupported ? 1 : 0);
        using var json = JsonDocument.Parse(capture.ToString());
        json.RootElement.GetProperty("IsSuccess").GetBoolean().Should().Be(!unsupported);
        capture.ToString().Should().NotContain(PlanRedactionServiceTests.Marker);
        File.ReadAllBytes(input).Should().Equal(before);
        File.Exists(output).Should().Be(!unsupported);
    }

    [Fact]
    public void RawReportCannotSilentlyIgnoreRequestedRedaction()
    {
        var old = Console.Out; var output = new StringWriter();
        int scanCode, refactorCode;
        try
        {
            Console.SetOut(output);
            scanCode = Program.Main(new[] { "--path", "unused.sqlplan", "--format", "json", "--redacted" });
            refactorCode = Program.Main(new[] { "refactor", "unused.sql", "--redacted" });
        }
        finally { Console.SetOut(old); }
        scanCode.Should().Be(2);
        refactorCode.Should().Be(2);
        output.ToString().Should().BeEmpty();
    }

    public void Dispose()
    {
        string path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp05Cli-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup target");
        Directory.Delete(path, true);
    }

    [Fact]
    public void DesktopBatchRedaction_StopsBeforeEnumeratingOrReportingRawNames()
    {
        string secretFile = Path.Combine(_directory, PlanRedactionServiceTests.Marker + ".sqlplan");
        File.WriteAllText(secretFile, PlanRedactionServiceTests.Fixture().ToString());
        var previousOut = Console.Out; var previousError = Console.Error;
        int previousCode = Environment.ExitCode;
        var capture = new StringWriter(); int exitCode; bool handled;
        try
        {
            Console.SetOut(capture); Console.SetError(capture);
            handled = Core.Services.CliService.HandleCommandLineArgs(new[] { "--batch", _directory, "--export", "obfuscated" });
            exitCode = Environment.ExitCode;
        }
        finally { Console.SetOut(previousOut); Console.SetError(previousError); Environment.ExitCode = previousCode; }
        handled.Should().BeTrue(); exitCode.Should().Be(1);
        capture.ToString().Should().Contain("暂不支持批量脱敏导出").And.NotContain(PlanRedactionServiceTests.Marker);
        Directory.GetFiles(_directory).Should().Equal(secretFile);
    }

    [Fact]
    public void RefactorFailureTextReport_StillDisclosesRawDiagnosticData()
    {
        string path = Path.Combine(_directory, "failure.txt");
        int code = Program.Main(new[] { "refactor", Path.Combine(_directory, "missing.sql"), "--dry-run", "--output", path });
        code.Should().Be(1);
        File.ReadAllText(path).Should().Contain(Core.Privacy.OutputPrivacy.RawNotice);
    }
}
