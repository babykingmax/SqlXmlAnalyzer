using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Refactoring;
using SqlXmlAnalyzer.Refactoring.Rules;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class RefactorSafetyIntegrationTests
{
    private static readonly XNamespace ShowplanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    [Fact]
    public void Analyze_GuiAdapter_KeepsSqlSeparateAndIncludesSafetyWarningsInVisibleDiagnostics()
    {
        const string sql = "SELECT LTRIM(Name) FROM Users;";
        var files = new PhysicalFileHandler();
        using var temporary = new TemporaryFileManager();
        var service = new PlanAnalysisService(CreateOrchestrator(files), files, temporary);
        var document = new XDocument(new XElement(ShowplanNs + "ShowPlanXML",
            new XElement(ShowplanNs + "BatchSequence", new XElement(ShowplanNs + "Batch",
                new XElement(ShowplanNs + "Statements", new XElement(ShowplanNs + "StmtSimple", new XAttribute("StatementText", sql)))))));
        var result = service.Analyze(document, ShowplanNs, "missing-imp04-plan.sqlplan");
        result.RefactoredSql.Should().Be(sql);
        result.WarningsText.Should().Contain("未产生 SQL 改写").And.Contain("REF_RULE_103_TRIM").And.Contain("前导空格").And.Contain("验证前提");
    }

    [Fact]
    public void Presentation_WhenEngineFails_ShowsErrorAndKeepsOriginalSqlWithoutInjectedComments()
    {
        const string sql = "SELECT 1;";
        var context = new RefactorContext(sql);
        context.Warn("Safety prerequisite warning");
        var failed = new RefactorResult("invalid candidate", false, new[] { "Unknown error; DUMP: synthetic.dmp */ SELECT 2;" }, context);
        var presentation = RefactorPresentation.FromResult(new(failed, false, null, null, Array.Empty<string>()), sql);
        presentation.Sql.Should().Be(sql);
        presentation.Notices.Should().Contain("synthetic.dmp").And.Contain("Safety prerequisite warning");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reporters_WhenReportCannotBeWritten_ThrowSoOrchestratorCanReportFailure(bool json)
    {
        using var temporary = new TemporaryFileManager();
        string directoryPath = temporary.CreatePath("ReportDirectory", ".json");
        Directory.CreateDirectory(directoryPath);
        try
        {
            IResultReporter reporter = json ? new JsonResultReporter() : new ConsoleResultReporter();
            var result = new RefactorResult("SELECT 1;", true, Array.Empty<string>(), new("SELECT 1;"));
            Action action = () => reporter.Report(result, true, directoryPath);
            action.Should().Throw<UnauthorizedAccessException>();
        }
        finally { Directory.Delete(directoryPath); }
    }

    [Fact]
    public void Execute_WhenUnknownEngineFailureOccurs_ReturnsDumpLocationWithoutWritingSource()
    {
        var files = new PhysicalFileHandler();
        using var temporary = new TemporaryFileManager();
        string sqlPath = temporary.CreatePath("UnknownFailure", ".sql");
        File.WriteAllText(sqlPath, "SELECT 1;");
        var diagnostics = new Refactoring.UnsafeRewriteProtectionTests.RecordingDiagnostics();
        var orchestrator = new ApplicationOrchestrator(new EmptyAnalysis(), new ThrowingEngine(), files, new SilentReporter(),
            NullLogger<ApplicationOrchestrator>.Instance, unexpectedErrors: diagnostics);
        var result = orchestrator.Execute(sqlPath);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("synthetic.dmp");
        result.ErrorException.Should().BeOfType<InvalidOperationException>();
        diagnostics.Calls.Should().Be(1);
        File.ReadAllText(sqlPath).Should().Be("SELECT 1;");
    }

    private static ApplicationOrchestrator CreateOrchestrator(IFileHandler files) => new(new EmptyAnalysis(),
        new SqlRefactoringEngine(new ISqlRefactorRule[] { new TrimRefactorRule(), new TableVariableRefactorRule() },
            new DefaultRuleFilter(), NullLogger<SqlRefactoringEngine>.Instance), files, new SilentReporter(), NullLogger<ApplicationOrchestrator>.Instance);

    private sealed class EmptyAnalysis : IAnalysisEngine
    {
        public AnalysisReport Analyze(string xmlContent) => new(Array.Empty<IAnalysisIssue>());
    }
    private sealed class SilentReporter : IResultReporter
    {
        public void Report(RefactorResult result) { }
        public void Report(RefactorResult result, bool isDryRun, string? outputPath) { }
    }
    private sealed class ThrowingEngine : IRefactoringEngine
    {
        public RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun) => throw new InvalidOperationException("unknown engine failure");
    }
}
