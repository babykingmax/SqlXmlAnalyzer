using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Compatibility;

public sealed class CliCompatibilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP28-cli-" + Guid.NewGuid().ToString("N"));
    private string Plan => Path.Combine(_directory, "plan.sqlplan");
    private string Sql => Path.Combine(_directory, "query.sql");
    public CliCompatibilityTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Plan, CompatibilityFixtures.Read("plan.sqlplan"));
        File.WriteAllText(Sql, CompatibilityFixtures.Read("query.sql"));
    }

    private static (int ExitCode, string Output, string Error) Capture(Func<int> command)
    {
        var output = new StringWriter(); var error = new StringWriter();
        var previousOutput = Console.Out; var previousError = Console.Error;
        try
        {
            Console.SetOut(output); Console.SetError(error);
            int result = command();
            return (result, output.ToString(), error.ToString());
        }
        finally { Console.SetOut(previousOutput); Console.SetError(previousError); Logger.Shutdown(); }
    }

    private static void PreservesFields(JsonElement baseline, JsonElement actual)
    {
        foreach (var field in baseline.EnumerateObject())
        {
            actual.TryGetProperty(field.Name, out var value).Should().BeTrue(field.Name);
            value.ValueKind.Should().Be(field.Value.ValueKind, field.Name);
        }
    }

    [Theory]
    [InlineData("--path", "--format")]
    [InlineData("-p", "-f")]
    public void Scan_OriginalAliasesAndArrayJsonRemainReadable(string pathFlag, string formatFlag)
    {
        var result = Capture(() => Program.Main([pathFlag, Plan, formatFlag, "json"]));
        result.ExitCode.Should().Be(0);
        using var current = JsonDocument.Parse(result.Output);
        using var baseline = JsonDocument.Parse(CompatibilityFixtures.Read("cli-scan-before.json"));
        current.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        PreservesFields(baseline.RootElement[0], current.RootElement[0]);
        var legacy = JsonSerializer.Deserialize<LegacyScanConsumer[]>(result.Output)!;
        legacy.Should().ContainSingle();
        legacy[0].FileName.Should().Be("plan.sqlplan");
        legacy[0].Status.Should().Be("Passed");
        legacy[0].MaxSubtreeCost.Should().Be(1);
        legacy[0].ContainsScans.Should().BeTrue();
        current.RootElement[0].GetProperty("MaxSubtreeCostState").GetString().Should().Be("Available");
    }

    [Fact]
    public void Read_ExistingObjectFieldsAndInputIdentityRemainAvailable()
    {
        var result = Capture(() => Program.Main(["read", Plan]));
        result.ExitCode.Should().Be(0);
        using var current = JsonDocument.Parse(result.Output);
        using var baseline = JsonDocument.Parse(CompatibilityFixtures.Read("cli-read-before.json"));
        PreservesFields(baseline.RootElement, current.RootElement);
        current.RootElement.GetProperty("Status").GetString().Should().Be("Success");
        current.RootElement.GetProperty("Kind").GetString().Should().Be("ExecutionPlanXml");
        current.RootElement.GetProperty("Plan").GetProperty("Batches").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Refactor_RetainsJsonFieldsAndExplicitCandidateOnlyOutcome()
    {
        byte[] original = File.ReadAllBytes(Sql);
        var result = Capture(() => Program.Main(["refactor", Sql, "--format", "json"]));
        result.ExitCode.Should().Be(0);
        using var current = JsonDocument.Parse(result.Output);
        using var baseline = JsonDocument.Parse(CompatibilityFixtures.Read("cli-refactor-before.json"));
        PreservesFields(baseline.RootElement, current.RootElement);
        current.RootElement.GetProperty("ProposalSchemaVersion").GetString().Should().Be("1");
        current.RootElement.GetProperty("Outcome").GetString().Should().Be("CandidateGenerated");
        current.RootElement.GetProperty("SourceWritten").GetBoolean().Should().BeFalse();
        current.RootElement.GetProperty("CanApply").GetBoolean().Should().BeFalse();
        File.ReadAllBytes(Sql).Should().Equal(original);
    }

    [Theory]
    [InlineData("help", 0)]
    [InlineData("threshold", 1)]
    [InlineData("invalid-input", 1)]
    [InlineData("unknown-option", 2)]
    [InlineData("future-config", 2)]
    [InlineData("cancel", 130)]
    public void ExitCodes_PreserveSuccessFailureUsageAndCancellation(string scenario, int expected)
    {
        string config = Path.Combine(_directory, "future.json");
        File.WriteAllText(config, "{\"ConfigurationSchemaVersion\":\"99\",\"Rules\":[]}");
        string invalid = Path.Combine(_directory, "invalid.sqlplan"); File.WriteAllText(invalid, "<unrelated />");
        var result = Capture(() => scenario switch
        {
            "help" => Program.Main(["--help"]),
            "threshold" => Program.Main(["--path", Plan, "--max-cost", "0.5", "--format", "json"]),
            "invalid-input" => Program.Main(["--path", invalid, "--format", "json"]),
            "unknown-option" => Program.Main(["--unknown-option"]),
            "future-config" => Program.Main(["--path", Plan, "--config", config, "--format", "json"]),
            _ => Program.RunReadCommand(Plan, new(), new(true))
        });
        result.ExitCode.Should().Be(expected);
        if (scenario == "future-config") result.Error.Should().Contain("CONFIG_SCHEMA_UNSUPPORTED");
        if (scenario == "cancel") result.Output.Should().BeEmpty();
    }

    [Fact]
    public void ReadOptions_UnknownFieldsFailWithUsageCodeInsteadOfSilentlyChangingBudgets()
    {
        string options = Path.Combine(_directory, "future-limits.json");
        File.WriteAllText(options, "{\"MaxBytes\":1024,\"UnknownFutureBudget\":123}");
        var result = Capture(() => Program.Main(["read", Plan, "--read-options", options]));
        result.ExitCode.Should().Be(2);
        result.Error.Should().Contain("INPUT_OPTIONS_INVALID");
        result.Output.Should().BeEmpty();
    }

    [Fact]
    public void JsonOutput_ExistingDestinationAndOriginalPlanRemainIntact()
    {
        string destination = Path.Combine(_directory, "report.json");
        File.WriteAllText(destination, "sole-old-report");
        byte[] original = File.ReadAllBytes(Plan);
        var result = Capture(() => Program.Main(["-p", Plan, "-f", "json", "-o", destination]));
        result.ExitCode.Should().Be(2);
        File.ReadAllText(destination).Should().Be("sole-old-report");
        File.ReadAllBytes(Plan).Should().Equal(original);
    }

    private sealed class LegacyScanConsumer
    {
        public string? FileName { get; set; }
        public string? Status { get; set; }
        public double MaxSubtreeCost { get; set; }
        public bool ContainsScans { get; set; }
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
