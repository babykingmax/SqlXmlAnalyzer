using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class RewriteProposalCliTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP18CLI-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_directory, "source.sql");
    private const string Sql = "SELECT * FROM Users WHERE Age + 10 > 50;";
    public RewriteProposalCliTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Source, Sql);
    }

    private (int Exit, JsonDocument Json) Run(params string[] extra)
    {
        var text = new StringWriter();
        var previous = Console.Out;
        int code;
        try
        {
            Console.SetOut(text);
            code = Program.Main(["refactor", Source, "--format", "json", .. extra]);
        }
        finally { Console.SetOut(previous); }
        return (code, JsonDocument.Parse(text.ToString()));
    }

    [Fact]
    public void Select_UsesStableProposalIdAndNeverWritesEvenWithValidPreview()
    {
        var initial = Run("--show-sql");
        using var first = initial.Json;
        initial.Exit.Should().Be(0);
        var root = first.RootElement;
        root.GetProperty("SourceHash").GetString().Should().Be(SqlTextHash.Compute(Sql));
        var proposal = root.GetProperty("Review").GetProperty("Proposals")[0];
        proposal.GetProperty("IsSelected").GetBoolean().Should().BeFalse();
        proposal.GetProperty("Diff").GetProperty("ReplacementText").GetString().Should().NotBeNull();
        root.GetProperty("Review").GetProperty("PreviewSql").GetString().Should().Be(Sql);
        string id = proposal.GetProperty("Id").GetString()!;
        var selection = Run("--select", id, "--show-sql");
        using var selected = selection.Json;
        selection.Exit.Should().Be(0);
        selected.RootElement.GetProperty("Review").GetProperty("PreviewSql").GetString().Should().Contain("Age > 40");
        selected.RootElement.GetProperty("Review").GetProperty("CanApply").GetBoolean().Should().BeFalse();
        selected.RootElement.GetProperty("SourceWritten").GetBoolean().Should().BeFalse();
        File.ReadAllText(Source).Should().Be(Sql);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public void Report_WithoutShowSql_OmitsDiffAndPreviewButRetainsReviewMetadata()
    {
        var output = Run();
        using var json = output.Json;
        output.Exit.Should().Be(0);
        var review = json.RootElement.GetProperty("Review");
        review.GetProperty("PreviewSql").ValueKind.Should().Be(JsonValueKind.Null);
        var item = review.GetProperty("Proposals")[0];
        item.GetProperty("Diff").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("RuleVersion").GetString().Should().NotBeNullOrWhiteSpace();
        item.GetProperty("Preconditions").GetArrayLength().Should().BeGreaterThan(0);
        item.GetProperty("Validation").GetProperty("CanApply").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Select_StaleIdFromDifferentSource_FailsAndPreservesChangedFile()
    {
        var output = Run();
        using var first = output.Json;
        string id = first.RootElement.GetProperty("Review").GetProperty("Proposals")[0].GetProperty("Id").GetString()!;
        File.AppendAllText(Source, " -- changed");
        var selection = Run("--select", id);
        using var failed = selection.Json;
        selection.Exit.Should().Be(1);
        failed.RootElement.GetProperty("IsSuccess").GetBoolean().Should().BeFalse();
        File.ReadAllText(Source).Should().Be(Sql + " -- changed");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void TextReport_SelectionMatchesJsonPreviewAndPreservesSource(int count, bool toFile)
    {
        const string source = "\r\n  -- preserve whitespace\r\nSELECT * FROM Users WHERE Name = N'admin' AND Age + 10 > 50;\r\n\r\n";
        File.WriteAllText(Source, source);
        byte[] original = File.ReadAllBytes(Source);
        var initial = Run("--show-sql");
        using var first = initial.Json;
        initial.Exit.Should().Be(0);
        var proposals = first.RootElement.GetProperty("Review").GetProperty("Proposals");
        proposals.GetArrayLength().Should().Be(2);
        var selection = proposals.EnumerateArray().Take(count)
            .SelectMany(p => new[] { "--select", p.GetProperty("Id").GetString()! }).ToArray();
        var selected = Run(["--show-sql", .. selection]);
        using var json = selected.Json;
        selected.Exit.Should().Be(0);
        string expected = json.RootElement.GetProperty("Review").GetProperty("PreviewSql").GetString()!;
        if (count == 0) expected.Should().Be(source);
        else if (count == 1) expected.Should().Contain("Age + 10 > 50").And.NotContain("Age > 40");
        else expected.Should().Contain("Age > 40");

        string outputPath = Path.Combine(_directory, "report.txt");
        var writer = new StringWriter();
        var previous = Console.Out;
        try
        {
            Console.SetOut(writer);
            // Exercise normal and compatibility dry-run paths through the actual CLI.
            string[] output = toFile ? ["--output", outputPath, "--dry-run"] : [];
            Program.Main(["refactor", Source, "--show-sql", .. selection, .. output]).Should().Be(0);
        }
        finally { Console.SetOut(previous); }
        string text = toFile ? File.ReadAllText(outputPath) : writer.ToString();
        text.Should().Contain($"已选择 {count} 项");
        const string marker = "┌── Selected review preview (not applied)";
        int start = text.LastIndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        string block = text[(text.IndexOf('\n', start) + 1)..];
        block.Should().StartWith(expected + Environment.NewLine + "└");
        text.Should().Contain(Environment.NewLine + source + Environment.NewLine + "└");
        File.ReadAllBytes(Source).Should().Equal(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReport_WithoutShowSql_DoesNotExpandSql(bool toFile)
    {
        string path = Path.Combine(_directory, "report.txt");
        var writer = new StringWriter();
        var previous = Console.Out;
        try
        {
            Console.SetOut(writer);
            string[] output = toFile ? ["--output", path] : [];
            Program.Main(["refactor", Source, .. output]).Should().Be(0);
        }
        finally { Console.SetOut(previous); }
        string text = toFile ? File.ReadAllText(path) : writer.ToString();
        text.Should().Contain("SQL 改写审核提案").And.NotContain("SQL diff")
            .And.NotContain("┌── Original SQL").And.NotContain("Selected review preview")
            .And.NotContain(Sql);
    }

    public void Dispose()
    {
        string full = Path.GetFullPath(_directory);
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP18CLI-");
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(full, recursive: true);
    }
}
