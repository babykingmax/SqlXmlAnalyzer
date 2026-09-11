using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.CLI;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class SqlSemanticCliTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP19Cli-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_directory, "source.sql");
    private string Suite => Path.Combine(_directory, "suite.json");
    private const string Sql = "SELECT Id FROM dbo.Users WHERE Age + 10 > 50;";
    public SqlSemanticCliTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Source, Sql);
        File.WriteAllText(Suite, JsonSerializer.Serialize(new SqlSemanticSuite([new("test", "")])));
    }

    private static (int Exit, string Out, string Error) Run(params string[] args)
    {
        var output = new StringWriter(); var error = new StringWriter();
        var previousOut = Console.Out; var previousError = Console.Error;
        try { Console.SetOut(output); Console.SetError(error); return (Program.Main(args), output.ToString(), error.ToString()); }
        finally { Console.SetOut(previousOut); Console.SetError(previousError); }
    }

    [Theory]
    [InlineData("semantic-compare")]
    [InlineData("rewrite-validate")]
    [InlineData("rewrite-apply")]
    public void Command_HelpAndIncompleteRequestsDoNotWrite(string command)
    {
        Run(command, "--help").Exit.Should().Be(0);
        Run(command, Source).Exit.Should().Be(2);
        File.ReadAllText(Source).Should().Be(Sql);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("suite")]
    [InlineData("candidate")]
    public void Compare_ReportCannotOverwriteAnyInput(string input)
    {
        string candidate = Path.Combine(_directory, "candidate.sql");
        File.WriteAllText(candidate, "SELECT 2;");
        string path = input == "source" ? Source : input == "suite" ? Suite : candidate;
        byte[] before = File.ReadAllBytes(path);
        Run("semantic-compare", Source, "--candidate", candidate, "--scenarios", Suite, "--output", path).Exit.Should().NotBe(0);
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Fact]
    public void Compare_ReportCannotOverwriteReadOptionsBeforeDatabaseWork()
    {
        string options = Path.Combine(_directory, "limits.json");
        File.WriteAllText(options, "{}");
        Run("semantic-compare", Source, "--candidate", Source, "--scenarios", Suite,
            "--read-options", options, "--output", options).Exit.Should().NotBe(0);
        File.ReadAllText(options).Should().Be("{}");
        File.ReadAllText(Source).Should().Be(Sql);
    }

    [Fact]
    public void Apply_RequiresReviewedHashesAndRejectsStaleSelectionBeforeDatabaseWork()
    {
        using var initial = JsonDocument.Parse(Run("refactor", Source, "--format", "json").Out);
        string id = initial.RootElement.GetProperty("Review").GetProperty("Proposals")[0].GetProperty("Id").GetString()!;
        var missing = Run("rewrite-apply", Source, "--select", id, "--scenarios", Suite);
        missing.Exit.Should().Be(2);
        var stale = Run("rewrite-apply", Source, "--select", id, "--scenarios", Suite,
            "--review-source-hash", "stale", "--review-preview-hash", "stale", "--review-suite-hash", "stale", "--acknowledge-scenarios");
        stale.Exit.Should().Be(2);
        stale.Error.Should().Contain("hash");
        File.ReadAllText(Source).Should().Be(Sql);
        Directory.GetFiles(_directory, "*.bak").Should().BeEmpty();
    }

    [Fact]
    public void Validate_UnknownOptionIsAnExpectedArgumentError()
    {
        Run("rewrite-validate", Source, "--force", "yes").Exit.Should().Be(2);
        File.ReadAllText(Source).Should().Be(Sql);
    }

    [Fact]
    public void Apply_ReplaysPlanConfigurationAndPassLimitBeforeCheckingReviewedHashes()
    {
        File.WriteAllText(Source, "SELECT * FROM dbo.Users WHERE Name=N'a' AND Age+10>50;");
        string plan = Path.Combine(_directory, "plan.sqlplan");
        using (var stream = typeof(SqlSemanticCliTests).Assembly.GetManifestResourceStream("SqlXmlAnalyzer.Tests.TestData.imp11_operator_facts.sqlplan")!)
        using (var reader = new StreamReader(stream)) File.WriteAllText(plan, reader.ReadToEnd());
        string configuration = Path.Combine(_directory, "rules.json");
        File.WriteAllText(configuration, "{\"Rules\":[{\"RuleId\":\"RULE_001_IMPLICIT_CONV\",\"Enabled\":false}]}");
        string[] replay = ["--plan", plan, "--config", configuration, "--max-passes", "1"];
        var generated = Run(["refactor", Source, "--format", "json", .. replay]);
        generated.Exit.Should().Be(0);
        using var initial = JsonDocument.Parse(generated.Out);
        var proposals = initial.RootElement.GetProperty("Review").GetProperty("Proposals");
        proposals.GetArrayLength().Should().Be(1);
        string id = proposals[0].GetProperty("Id").GetString()!;
        var replayed = Run(["rewrite-apply", Source, "--select", id, "--scenarios", Suite,
            "--review-source-hash", "stale", "--review-preview-hash", "stale", "--review-suite-hash", "stale",
            "--acknowledge-scenarios", .. replay]);
        replayed.Exit.Should().Be(2);
        replayed.Error.Should().Contain("审核 hash").And.NotContain("UnknownProposal");
        var review = SqlSemanticCommand.BuildReview(File.ReadAllText(Source), [id], plan, configuration, 1);
        review.Proposals.Should().ContainSingle(p => p.Id == id && p.IsSelected);
        review.PreviewSql.Should().Contain("Age > 40").And.Contain("N'a'");
        File.ReadAllText(Source).Should().Contain("Age+10>50");
    }

    [Fact]
    public void Replay_PreservesSelectionsBeyondTheDefaultPassLimit()
    {
        string source = "SELECT a.Id FROM dbo.A a WHERE " + string.Join(" AND ", Enumerable.Range(1, 6)
            .Select(i => $"EXISTS (SELECT 1 FROM dbo.B b{i} WHERE b{i}.Id=a.Id)")) + ";";
        var generated = SqlSemanticCommand.BuildReview(source, [], null, null, 6);
        generated.Proposals.Length.Should().Be(6);
        var ids = generated.Proposals.Select(p => p.Id).ToArray();
        var selected = SqlSemanticCommand.BuildReview(source, ids, null, null, 6);
        selected.Proposals.Should().OnlyContain(p => p.IsSelected);
        selected.PreviewSql.Should().NotContain("EXISTS");
        Action wrongLimit = () => SqlSemanticCommand.BuildReview(source, ids, null, null, 5);
        wrongLimit.Should().Throw<InvalidDataException>().WithMessage("*--max-passes*");
    }

    [Fact]
    public void Replay_InvalidPlanAndReadBudgetBlockBeforeDatabaseValidation()
    {
        string plan = Path.Combine(_directory, "plan.sqlplan");
        File.WriteAllText(plan, "<ShowPlanXML>" + new string(' ', 10000));
        Action replay = () => SqlSemanticCommand.BuildReview(Sql, [], plan, null, 5,
            new Core.Services.DocumentReadOptions { MaxBytes = 32 });
        replay.Should().Throw<InvalidDataException>();
        File.ReadAllText(Source).Should().Be(Sql);
    }

    [Theory]
    [InlineData("semantic-compare", false)]
    [InlineData("semantic-compare", true)]
    [InlineData("rewrite-validate", false)]
    [InlineData("rewrite-apply", false)]
    public void Command_OversizedInputIsRejectedBeforeParsingOrProposalGeneration(string command, bool largeCandidate)
    {
        string candidate = Path.Combine(_directory, "candidate.sql");
        File.WriteAllText(candidate, "SELECT 1;");
        string large = largeCandidate ? candidate : Source;
        // Invalid SQL would otherwise fail in the parser, before the old late budget check.
        File.WriteAllText(large, new string('x', SqlSemanticSandboxPolicy.MaxSqlCharacters + 1));
        var args = new List<string> { command, Source, "--scenarios", Suite };
        args.AddRange(command == "semantic-compare" ? ["--candidate", candidate] : ["--select", "unreachable"]);
        if (command == "rewrite-apply") args.AddRange(["--review-source-hash", "unused", "--review-preview-hash", "unused",
            "--review-suite-hash", "unused", "--acknowledge-scenarios"]);
        var result = Run(args.ToArray());
        result.Exit.Should().Be(2);
        result.Error.Should().Contain("SqlInputBudget:");
        new FileInfo(large).Length.Should().Be(SqlSemanticSandboxPolicy.MaxSqlCharacters + 1);
        Directory.GetFiles(_directory, "*.bak").Should().BeEmpty();
    }

    [Theory]
    [InlineData("semantic-compare")]
    [InlineData("rewrite-validate")]
    [InlineData("rewrite-apply")]
    public void Command_MutatingObserverIsRejectedBeforeValidationOrWriteback(string command)
    {
        File.WriteAllText(Suite, JsonSerializer.Serialize(new SqlSemanticSuite([new("case", "", "DELETE FROM dbo.Users;")])));
        using var proposal = JsonDocument.Parse(Run("refactor", Source, "--format", "json").Out);
        string id = proposal.RootElement.GetProperty("Review").GetProperty("Proposals")[0].GetProperty("Id").GetString()!;
        var args = new List<string> { command, Source, "--scenarios", Suite };
        args.AddRange(command == "semantic-compare" ? ["--candidate", Source] : ["--select", id]);
        if (command == "rewrite-apply") args.AddRange(["--review-source-hash", "unused", "--review-preview-hash", "unused",
            "--review-suite-hash", "unused", "--acknowledge-scenarios"]);
        var result = Run(args.ToArray());
        result.Exit.Should().Be(2);
        result.Error.Should().Contain("ObserverReadOnly");
        File.ReadAllText(Source).Should().Be(Sql);
        Directory.GetFiles(_directory, "*.bak").Should().BeEmpty();
    }

    public void Dispose()
    {
        string full = Path.GetFullPath(_directory);
        if (!full.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP19Cli-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(full, true);
    }
}
