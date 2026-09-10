using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Core.Privacy;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class CliScanHardeningTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-ScanHardening-" + Guid.NewGuid().ToString("N"));
    private string PlanPath => Path.Combine(_directory, "input.sqlplan");

    public CliScanHardeningTests()
    {
        Directory.CreateDirectory(_directory);
        WritePlan("0.25");
    }

    private void WritePlan(string? cost, string physicalOp = "Index Seek", string payload = "")
    {
        string attribute = cost == null ? "" : $"EstimatedTotalSubtreeCost='{cost}'";
        File.WriteAllText(PlanPath, $"""
            <ShowPlanXML xmlns="{PlanRedactionService.ShowPlanNamespace}"><BatchSequence><Batch><Statements>
            <StmtSimple StatementText="SELECT 1"><QueryPlan>
            <RelOp NodeId="0" PhysicalOp="{physicalOp}" LogicalOp="{physicalOp}" EstimateRows="1" {attribute}>{payload}</RelOp>
            </QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>
            """);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scan_WhenReportBudgetIsReached_ReturnsSmallExplicitFailure(bool metrics)
    {
        File.WriteAllText(PlanPath, DiagnosticReportBudgetAndScopeTests.Operators(1000, metrics).ToString());
        var result = Program.ScanPlanFile(PlanPath, null, null, false, new Core.Services.DocumentReadOptions(),
            CancellationToken.None, () => new Core.Rules.RuleEngine());
        if (metrics)
        {
            result.Status.Should().Be("Failed");
            result.FailureMessage.Should().StartWith("REPORT_BUDGET_EXCEEDED");
            result.Plan.Should().BeNull(); result.Diagnostics.Should().BeNull(); result.DiagnosticReport.Should().BeNull();
            JsonSerializer.Serialize(result).Length.Should().BeLessThan(20_000, "failed JSON must not re-expand the rejected plan");
        }
        else
        {
            result.Status.Should().Be("Passed");
            result.DiagnosticReport!.FactCount.Should().BeLessThan(10_000);
        }
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e309")]
    [InlineData("-1")]
    [InlineData("1,2")]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData(null)]
    public void Main_WhenCostThresholdIsInvalid_RejectsArgumentsWithoutPublishing(string? value)
    {
        string output = Path.Combine(_directory, "report.json");
        var args = new List<string> { "--path", PlanPath, "--format", "json", "--output", output, "--max-cost" };
        if (value != null) args.Add(value);
        var result = Run(args.ToArray());
        result.Code.Should().Be(2);
        result.Error.Should().Contain("--max-cost");
        File.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e309")]
    [InlineData("-1")]
    [InlineData("1,2")]
    [InlineData("invalid")]
    public void Main_WhenSourceCostIsInvalid_EmitsFailedSerializableResult(string cost)
    {
        WritePlan(cost);
        var result = Run("--path", PlanPath, "--format", "json");
        using var json = JsonDocument.Parse(result.Output);
        var scan = json.RootElement[0];
        result.Code.Should().Be(1);
        scan.GetProperty("FailureMessage").GetString().Should().Contain("PLAN_COST_INVALID");
        scan.GetProperty("MaxSubtreeCostState").GetString().Should().Be("Invalid");
        double.IsFinite(scan.GetProperty("MaxSubtreeCost").GetDouble()).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.25")]
    public void Main_WhenRequiredCostIsMissing_DoesNotPassTheThreshold(string? parentCost)
    {
        WritePlan(parentCost, payload: "<RelOp NodeId='1' PhysicalOp='Index Seek' LogicalOp='Index Seek' />");
        var result = Run("--path", PlanPath, "--format", "json", "--max-cost", "100");
        result.Code.Should().Be(1);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement[0].GetProperty("FailureMessage").GetString().Should().Contain("PLAN_COST_UNAVAILABLE");
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("0.25", 0)]
    [InlineData("2.5e-1", 0)]
    public void Main_WhenCostIsFinite_EnforcesThreshold(string threshold, int expectedCode)
    {
        var result = Run("--path", PlanPath, "--format", "json", "--max-cost", threshold);
        result.Code.Should().Be(expectedCode);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement[0].GetProperty("MaxSubtreeCost").GetDouble().Should().Be(0.25);
    }

    [Theory]
    [InlineData("--path")]
    [InlineData("--config")]
    [InlineData("--format")]
    [InlineData("--output")]
    [InlineData("--exclude")]
    [InlineData("-m")]
    public void Main_WhenOptionValueIsMissing_RejectsArguments(string option)
    {
        var result = Run("--path", PlanPath, option);
        result.Code.Should().Be(2);
        result.Error.Should().Contain(option);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Theory]
    [InlineData("--max-cots")]
    [InlineData("--block-scan")]
    public void Main_WhenGateOptionIsMisspelled_RejectsInsteadOfSilentlyDisablingGate(string option)
    {
        string output = Path.Combine(_directory, "report.json");
        var result = Run("--path", PlanPath, "--format", "json", "--output", output, option);
        result.Code.Should().Be(2);
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public void Main_WhenValueIsAnotherOption_DoesNotConsumeTheOptionAsAValue()
    {
        var result = Run("--path", PlanPath, "--exclude", "--block-scans");
        result.Code.Should().Be(2);
        result.Error.Should().Contain("--exclude");
    }

    [Fact]
    public void Main_WhenCostIsMissingWithoutThreshold_DisplaysUnknownInsteadOfZero()
    {
        WritePlan(null);
        var result = Run("--path", PlanPath);
        result.Code.Should().Be(0);
        result.Output.Should().Contain("开销: N/A (Missing)").And.NotContain("开销: 0.0000");
    }

    [Theory]
    [InlineData("0.25", "Available")]
    [InlineData("1e309", "Invalid")]
    public void Scan_WhenLegacyCostAttributeIsUsed_AppliesTheSameValidation(string cost, string expectedState)
    {
        WritePlan(cost);
        File.WriteAllText(PlanPath, File.ReadAllText(PlanPath).Replace("EstimatedTotalSubtreeCost", "SubTreeCost", StringComparison.Ordinal));
        var result = Program.ScanPlanFile(PlanPath, null, null, false, new(), default);
        result.MaxSubtreeCostState.Should().Be(expectedState);
        double.IsFinite(result.MaxSubtreeCost).Should().BeTrue();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("hardlink")]
    [InlineData("existing-report")]
    [InlineData("directory-scan")]
    public void Main_WhenOutputAlreadyExists_PreservesEveryFile(string mode)
    {
        string output = mode is "source" or "directory-scan" ? Path.Combine(_directory, ".", "input.sqlplan") : Path.Combine(_directory, "report.json");
        if (mode == "hardlink") CreateHardLink(output, PlanPath, IntPtr.Zero).Should().BeTrue();
        if (mode == "existing-report") File.WriteAllText(output, "previous report");
        byte[] inputBefore = File.ReadAllBytes(PlanPath), outputBefore = File.ReadAllBytes(output);
        var result = Run("--path", mode == "directory-scan" ? _directory : PlanPath, "--format", "json", "--output", output);
        result.Code.Should().Be(2);
        File.ReadAllBytes(PlanPath).Should().Equal(inputBefore);
        File.ReadAllBytes(output).Should().Equal(outputBefore);
        Directory.GetFiles(_directory, ".tmp.*").Should().BeEmpty();
        Directory.GetFiles(_directory, "*.partial").Should().BeEmpty();
    }

    [Theory]
    [InlineData("console")]
    [InlineData("unknown")]
    public void Main_WhenFormatCannotProduceFile_RejectsInsteadOfWritingEmptyReport(string format)
    {
        string output = Path.Combine(_directory, "report.txt");
        var result = Run("--path", PlanPath, "--format", format, "--output", output);
        result.Code.Should().Be(2);
        File.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData("json")]
    [InlineData("junit")]
    public void Main_WhenOutputIsNew_PublishesCompleteReport(string format)
    {
        string output = Path.Combine(_directory, "report." + format);
        byte[] inputBefore = File.ReadAllBytes(PlanPath);
        Run("--path", PlanPath, "--format", format, "--output", output).Code.Should().Be(0);
        string contents = File.ReadAllText(output);
        if (format == "json")
        {
            using var json = JsonDocument.Parse(contents);
            json.RootElement.GetArrayLength().Should().Be(1);
        }
        else SafeXmlHelper.ParseSafe(contents).Descendants("testcase").Should().ContainSingle();
        File.ReadAllBytes(PlanPath).Should().Equal(inputBefore);
        Directory.GetFiles(_directory, ".tmp.*").Should().BeEmpty();
        Directory.GetFiles(_directory, "*.partial").Should().BeEmpty();
    }

    [Fact]
    public void Scan_WhenPartitionedClusteredScanHasSeekPredicates_StillBlocksScan()
    {
        // Microsoft documents SeekPredicates on partitioned Clustered Index Scan.
        // It restricts partitions without changing the physical operator to a seek.
        WritePlan("0.25", "Clustered Index Scan", "<IndexScan Partitioned='true'><SeekPredicates /></IndexScan>");
        var result = Program.ScanPlanFile(PlanPath, null, null, true, new(), default);
        result.Status.Should().Be("Failed");
        result.ContainsScans.Should().BeTrue();
        result.ScannedOperators.Should().ContainSingle().Which.PhysicalOp.Should().Be("Clustered Index Scan");
    }

    [Theory]
    [InlineData("Table Scan")]
    [InlineData("Index Scan")]
    [InlineData("Clustered Index Scan")]
    public void Scan_WhenChildHasSeekPredicates_StillBlocksTheParentScan(string physicalOp)
    {
        WritePlan("0.25", physicalOp, "<RelOp NodeId='1' PhysicalOp='Index Seek' LogicalOp='Index Seek' EstimatedTotalSubtreeCost='0.1'><IndexScan><SeekPredicates /></IndexScan></RelOp>");
        var result = Program.ScanPlanFile(PlanPath, null, null, true, new(), default);
        result.Status.Should().Be("Failed");
        result.ContainsScans.Should().BeTrue();
        result.ScannedOperators.Should().ContainSingle().Which.NodeId.Should().Be("0");
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter(); var error = new StringWriter();
        TextWriter oldOutput = Console.Out, oldError = Console.Error;
        try
        {
            Console.SetOut(output); Console.SetError(error);
            return (Program.Main(args), output.ToString(), error.ToString());
        }
        finally { Console.SetOut(oldOutput); Console.SetError(oldError); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    public void Dispose()
    {
        string directory = Path.GetFullPath(_directory);
        if (!directory.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-ScanHardening-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(directory, recursive: true);
    }
}
