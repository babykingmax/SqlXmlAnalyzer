using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Application
{
    public class ApplicationOrchestratorTests
    {
        private readonly FakeFileHandler _fileHandler;
        private readonly FakeAnalysisEngine _analysisEngine;
        private readonly FakeRefactoringEngine _refactoringEngine;
        private readonly FakeResultReporter _reporter;
        private readonly ApplicationOrchestrator _orchestrator;

        public ApplicationOrchestratorTests()
        {
            _fileHandler = new FakeFileHandler();
            _analysisEngine = new FakeAnalysisEngine();
            _refactoringEngine = new FakeRefactoringEngine();
            _reporter = new FakeResultReporter();
            _orchestrator = new ApplicationOrchestrator(
                _analysisEngine,
                _refactoringEngine,
                _fileHandler,
                _reporter,
                NullLogger<ApplicationOrchestrator>.Instance
            );
        }

        [Fact]
        public void Execute_WithMissingSqlFile_ShouldReturnFailure()
        {
            // Arrange & Act
            var result = _orchestrator.Execute("missing.sql");

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("SQL file not found");
            result.Result.Should().BeNull();
        }

        [Fact]
        public void Execute_WithExistingSqlFileAndNoPlan_ShouldRunRefactoring()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";
            var expectedResult = new RefactorResult("SELECT * FROM Users WHERE 1=1", true, new List<string>(), new RefactorContext("SELECT * FROM Users"));
            _refactoringEngine.Result = expectedResult;

            // Act
            var result = _orchestrator.Execute("query.sql");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Result.Should().Be(expectedResult);
            _refactoringEngine.LastSql.Should().Be("SELECT * FROM Users");
            _refactoringEngine.LastReport.Should().NotBeNull();
            _refactoringEngine.LastReport!.Issues.Should().BeEmpty();
            _reporter.ReportedResult.Should().Be(expectedResult);
        }

        [Fact]
        public void Execute_WithExistingSqlAndXmlPlan_ShouldRunAnalysisAndRefactoring()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";
            _fileHandler.Files["plan.sqlplan"] = "<xml>plan</xml>";

            var fakeIssue = new FakeAnalysisIssue();
            var expectedReport = new AnalysisReport(new List<IAnalysisIssue> { fakeIssue });
            _analysisEngine.Report = expectedReport;

            var expectedResult = new RefactorResult("SELECT * FROM Users WHERE 1=1", true, new List<string>(), new RefactorContext("SELECT * FROM Users"));
            _refactoringEngine.Result = expectedResult;

            // Act
            var result = _orchestrator.Execute("query.sql", "plan.sqlplan");

            // Assert
            result.IsSuccess.Should().BeTrue();
            _analysisEngine.LastXmlContent.Should().Be("<xml>plan</xml>");
            _refactoringEngine.LastReport.Should().Be(expectedReport);
        }

        [Fact]
        public void Execute_WithMissingPlanFile_ShouldLogWarningAndProceedWithEmptyReport()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";

            // Act
            var result = _orchestrator.Execute("query.sql", "missing.sqlplan");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Warnings.Should().Contain(w => w.Contains("XML plan file not found"));
            _refactoringEngine.LastReport.Should().NotBeNull();
            _refactoringEngine.LastReport!.Issues.Should().BeEmpty();
        }

        [Fact]
        public void Execute_WhenSuccessfulAndNotDryRun_ShouldWriteBackToFile()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";
            var expectedResult = new RefactorResult("SELECT * FROM Users WHERE 1=1", true, new List<string>(), new RefactorContext("SELECT * FROM Users"));
            _refactoringEngine.Result = expectedResult;

            // Act
            var result = _orchestrator.Execute("query.sql", isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _fileHandler.Files["query.sql"].Should().Be("SELECT * FROM Users WHERE 1=1");
        }

        [Fact]
        public void Execute_WhenSuccessfulAndDryRun_ShouldNotWriteBackToFile()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";
            var expectedResult = new RefactorResult("SELECT * FROM Users WHERE 1=1", true, new List<string>(), new RefactorContext("SELECT * FROM Users"));
            _refactoringEngine.Result = expectedResult;

            // Act
            var result = _orchestrator.Execute("query.sql", isDryRun: true);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _fileHandler.Files["query.sql"].Should().Be("SELECT * FROM Users");
        }

        [Fact]
        public void Execute_WhenRefactoringEngineFails_ShouldNotWriteBackToFile()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";
            var expectedResult = new RefactorResult("SELECT * FROM Users", false, new List<string> { "Refactoring error" }, new RefactorContext("SELECT * FROM Users"));
            _refactoringEngine.Result = expectedResult;

            // Act
            var result = _orchestrator.Execute("query.sql", isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeFalse();
            _fileHandler.Files["query.sql"].Should().Be("SELECT * FROM Users");
        }

        [Fact]
        public void Execute_WhenExceptionThrown_ShouldCatchAndReturnFailureResult()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";
            _refactoringEngine.ThrowException = new InvalidOperationException("Fatal error");

            // Act
            var result = _orchestrator.Execute("query.sql");

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Orchestration pipeline crashed");
            result.ErrorException.Should().BeOfType<InvalidOperationException>();
        }

        [Fact]
        public void Execute_WithUnauthorizedAccessException_ShouldReturnFriendlyErrorMessage()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";
            _fileHandler.ThrowException = new UnauthorizedAccessException("Permission denied");

            // Act
            var result = _orchestrator.Execute("query.sql");

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Access denied to file");
            result.ErrorMessage.Should().Contain("permissions");
            result.ErrorException.Should().BeOfType<UnauthorizedAccessException>();
        }

        [Fact]
        public void Execute_WithIOException_ShouldReturnFriendlyErrorMessage()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";
            _fileHandler.ThrowException = new IOException("File locked");

            // Act
            var result = _orchestrator.Execute("query.sql");

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("IO Exception while reading or writing files");
            result.ErrorMessage.Should().Contain("locked");
            result.ErrorException.Should().BeOfType<IOException>();
        }

        [Fact]
        public void Execute_WithInvalidXmlPlan_ShouldReturnFriendlyErrorMessage()
        {
            // Arrange
            _fileHandler.Files["query.sql"] = "SELECT * FROM Users";
            _fileHandler.Files["plan.sqlplan"] = "not a valid xml <xml>";

            // Act
            var result = _orchestrator.Execute("query.sql", "plan.sqlplan");

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Invalid XML execution plan");
            result.ErrorMessage.Should().Contain("valid XML document");
            result.ErrorException.Should().BeOfType<System.Xml.XmlException>();
        }

        // Fakes / Stubs
        private class FakeFileHandler : IFileHandler
        {
            public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Exception? ThrowException { get; set; }

            public string ReadAllText(string path)
            {
                if (ThrowException != null) throw ThrowException;
                if (Files.TryGetValue(path, out var content)) return content;
                throw new FileNotFoundException($"File not found: {path}");
            }

            public void WriteAllText(string path, string contents)
            {
                if (ThrowException != null) throw ThrowException;
                Files[path] = contents;
            }

            public bool Exists(string path)
            {
                return Files.ContainsKey(path);
            }
        }

        private class FakeAnalysisEngine : IAnalysisEngine
        {
            public AnalysisReport Report { get; set; } = new(new List<IAnalysisIssue>());
            public string? LastXmlContent { get; private set; }

            public AnalysisReport Analyze(string xmlContent)
            {
                LastXmlContent = xmlContent;
                try
                {
                    if (!string.IsNullOrEmpty(xmlContent))
                    {
                        System.Xml.Linq.XDocument.Parse(xmlContent);
                    }
                }
                catch (Exception ex)
                {
                    return new AnalysisReport(new List<IAnalysisIssue>
                    {
                        new FakeAnalysisIssue
                        {
                            IssueType = "PARSE_ERROR",
                            Description = $"Failed to parse execution plan: {ex.Message}",
                            Severity = IssueSeverity.Critical
                        }
                    });
                }
                return Report;
            }

            private class FakeAnalysisIssue : IAnalysisIssue
            {
                public string IssueType { get; set; } = "";
                public string Description { get; set; } = "";
                public IssueSeverity Severity { get; set; } = IssueSeverity.Warning;
                public string? TableName { get; set; }
                public string? ColumnName { get; set; }
            }
        }

        private class FakeRefactoringEngine : IRefactoringEngine
        {
            public RefactorResult Result { get; set; } = new("SELECT 1;", true, new List<string>(), new RefactorContext("SELECT 1;"));
            public Exception? ThrowException { get; set; }
            public string? LastSql { get; private set; }
            public AnalysisReport? LastReport { get; private set; }
            public RefactorOptions? LastOptions { get; private set; }
            public bool? LastIsDryRun { get; private set; }

            public RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun)
            {
                if (ThrowException != null) throw ThrowException;
                LastSql = sql;
                LastReport = report;
                LastOptions = options;
                LastIsDryRun = isDryRun;
                return Result;
            }
        }

        private class FakeResultReporter : IResultReporter
        {
            public RefactorResult? ReportedResult { get; private set; }
            public bool? LastIsDryRun { get; private set; }
            public string? LastOutputPath { get; private set; }

            public void Report(RefactorResult result)
            {
                ReportedResult = result;
            }

            public void Report(RefactorResult result, bool isDryRun, string? outputPath = null)
            {
                ReportedResult = result;
                LastIsDryRun = isDryRun;
                LastOutputPath = outputPath;
            }
        }

        private class FakeAnalysisIssue : IAnalysisIssue
        {
            public string IssueType => "FAKE_RULE";
            public string Description => "A fake issue for testing";
            public IssueSeverity Severity => IssueSeverity.Warning;
            public string? TableName => null;
            public string? ColumnName => null;
        }
    }
}
