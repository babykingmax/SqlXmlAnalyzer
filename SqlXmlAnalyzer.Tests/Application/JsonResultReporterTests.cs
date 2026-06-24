using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Models;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Application
{
    public class JsonResultReporterTests : IDisposable
    {
        private readonly string _tempOutputPath;

        public JsonResultReporterTests()
        {
            _tempOutputPath = Path.Combine(Path.GetTempPath(), $"report_{Guid.NewGuid()}.json");
        }

        public void Dispose()
        {
            if (File.Exists(_tempOutputPath))
            {
                File.Delete(_tempOutputPath);
            }
        }

        [Fact]
        public void Report_ShouldWriteValidJsonToConsole_WhenOutputPathIsNull()
        {
            // Arrange
            var context = new RefactorContext("SELECT * FROM Users WHERE 1=1");
            context.RecordChange("RULE_1", "Optimized comparison");

            var result = new RefactorResult("SELECT * FROM Users WHERE 1=1", true, new List<string>(), context)
            {
                TimeElapsedMs = 123.45,
                PassesCount = 2
            };

            var reporter = new JsonResultReporter { ShowSql = true };

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
            var outputJson = stringWriter.ToString();
            outputJson.Should().NotBeNullOrEmpty();

            var doc = JsonDocument.Parse(outputJson);
            var root = doc.RootElement;

            root.GetProperty("IsSuccess").GetBoolean().Should().BeTrue();
            root.GetProperty("IsDryRun").GetBoolean().Should().BeTrue();
            root.GetProperty("HasChanges").GetBoolean().Should().BeTrue();
            root.GetProperty("TimeElapsedMs").GetDouble().Should().Be(123.45);
            root.GetProperty("OriginalSqlLength").GetInt32().Should().Be("SELECT * FROM Users WHERE 1=1".Length);
            root.GetProperty("RefactoredSqlLength").GetInt32().Should().Be("SELECT * FROM Users WHERE 1=1".Length);
            root.GetProperty("PassesCount").GetInt32().Should().Be(2);

            var changes = root.GetProperty("Changes");
            changes.GetArrayLength().Should().Be(1);
            changes[0].GetProperty("RuleId").GetString().Should().Be("RULE_1");
            changes[0].GetProperty("Description").GetString().Should().Be("Optimized comparison");

            root.GetProperty("OriginalSql").GetString().Should().Be("SELECT * FROM Users WHERE 1=1");
            root.GetProperty("RefactoredSql").GetString().Should().Be("SELECT * FROM Users WHERE 1=1");
        }

        [Fact]
        public void Report_ShouldNotIncludeSql_WhenShowSqlIsFalse()
        {
            // Arrange
            var context = new RefactorContext("SELECT * FROM Users");
            var result = new RefactorResult("SELECT * FROM Users", true, new List<string>(), context);
            var reporter = new JsonResultReporter { ShowSql = false };

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
            var outputJson = stringWriter.ToString();
            var doc = JsonDocument.Parse(outputJson);
            var root = doc.RootElement;

            root.GetProperty("OriginalSql").ValueKind.Should().Be(JsonValueKind.Null);
            root.GetProperty("RefactoredSql").ValueKind.Should().Be(JsonValueKind.Null);
        }

        [Fact]
        public void Report_ShouldWriteToFile_WhenOutputPathIsProvided()
        {
            // Arrange
            var context = new RefactorContext("SELECT 1");
            var result = new RefactorResult("SELECT 1", true, new List<string>(), context)
            {
                TimeElapsedMs = 5.0,
                PassesCount = 1
            };
            var reporter = new JsonResultReporter { ShowSql = true };

            // Act
            reporter.Report(result, isDryRun: false, outputPath: _tempOutputPath);

            // Assert
            File.Exists(_tempOutputPath).Should().BeTrue();
            var fileContent = File.ReadAllText(_tempOutputPath);
            fileContent.Should().NotBeNullOrEmpty();

            var doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;
            root.GetProperty("TimeElapsedMs").GetDouble().Should().Be(5.0);
            root.GetProperty("OriginalSqlLength").GetInt32().Should().Be("SELECT 1".Length);
        }
    }
}
