using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Models;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Application
{
    public class ConsoleResultReporterTests : IDisposable
    {
        private readonly string _tempOutputPath;

        public ConsoleResultReporterTests()
        {
            _tempOutputPath = Path.Combine(Path.GetTempPath(), $"console_report_{Guid.NewGuid()}.txt");
        }

        public void Dispose()
        {
            if (File.Exists(_tempOutputPath))
            {
                File.Delete(_tempOutputPath);
            }
        }

        [Fact]
        public void Report_ShouldWriteStatusCardToConsole()
        {
            // Arrange
            var context = new RefactorContext("SELECT * FROM Users");
            var result = new RefactorResult("SELECT * FROM Users", true, new List<string>(), context)
            {
                TimeElapsedMs = 12.3,
                PassesCount = 1
            };

            var reporter = new ConsoleResultReporter { ShowSql = false };

            var stringWriter = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(stringWriter);

            try
            {
                // Act
                reporter.Report(result, isDryRun: true, outputPath: null);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            // Assert
            var output = stringWriter.ToString();
            output.Should().Contain("┌──────────────────────────────────────────────────────────┐");
            output.Should().Contain("│                    REFACTORING REPORT                    │");
            output.Should().Contain("Status: SUCCESS");
            output.Should().Contain("Mode:   Dry-Run (No files modified)");
            output.Should().Contain("No refactoring changes were applied.");
        }

        [Fact]
        public void Report_ShouldIncludeAlignedChanges_WhenChangesExist()
        {
            // Arrange
            var context = new RefactorContext("SELECT * FROM Users");
            context.RecordChange("SubqueryToJoinRule", "Optimized IN subquery");
            context.RecordChange("ExistsToJoinRule", "Converted EXISTS subquery");

            var result = new RefactorResult("SELECT * FROM Users", true, new List<string>(), context);
            var reporter = new ConsoleResultReporter { ShowSql = false };

            var stringWriter = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(stringWriter);

            try
            {
                // Act
                reporter.Report(result, isDryRun: false, outputPath: null);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            // Assert
            var output = stringWriter.ToString();
            output.Should().Contain("[Applied Changes]");
            output.Should().Contain("SubqueryToJoinRule       : Optimized IN subquery");
            output.Should().Contain("ExistsToJoinRule         : Converted EXISTS subquery");
        }

        [Fact]
        public void Report_ShouldIncludeWarnings_WhenWarningsExist()
        {
            // Arrange
            var context = new RefactorContext("SELECT * FROM Users");
            context.Warn("Test warning string here");

            var result = new RefactorResult("SELECT * FROM Users", true, new List<string>(), context);
            var reporter = new ConsoleResultReporter { ShowSql = false };

            var stringWriter = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(stringWriter);

            try
            {
                // Act
                reporter.Report(result, isDryRun: false, outputPath: null);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            // Assert
            var output = stringWriter.ToString();
            output.Should().Contain("[Warnings]");
            output.Should().Contain("! Test warning string here");
        }

        [Fact]
        public void Report_ShouldWriteToFile_WhenOutputPathIsProvided()
        {
            // Arrange
            var context = new RefactorContext("SELECT * FROM Users");
            context.RecordChange("SubqueryToJoinRule", "Optimized IN subquery");

            var result = new RefactorResult("SELECT * FROM Users", true, new List<string>(), context);
            var reporter = new ConsoleResultReporter { ShowSql = true };

            // Act
            reporter.Report(result, isDryRun: true, outputPath: _tempOutputPath);

            // Assert
            File.Exists(_tempOutputPath).Should().BeTrue();
            var fileContent = File.ReadAllText(_tempOutputPath);
            fileContent.Should().Contain("┌──────────────────────────────────────────────────────────┐");
            fileContent.Should().Contain("│                    REFACTORING REPORT                    │");
            fileContent.Should().Contain("Status: SUCCESS");
            fileContent.Should().Contain("Mode:   Dry-Run (No files modified)");
            fileContent.Should().Contain("SubqueryToJoinRule       : Optimized IN subquery");
            fileContent.Should().Contain("Original SQL");
            fileContent.Should().Contain("Refactored SQL");
        }
    }
}
