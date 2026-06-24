using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.CLI;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Application
{
    public class CliProgramTests : IDisposable
    {
        private readonly string _tempSqlFile;
        private readonly string _tempPlanFile;

        public CliProgramTests()
        {
            _tempSqlFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.sql");
            _tempPlanFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.sqlplan");
        }

        public void Dispose()
        {
            if (File.Exists(_tempSqlFile)) File.Delete(_tempSqlFile);
            if (File.Exists(_tempPlanFile)) File.Delete(_tempPlanFile);
        }

        [Fact]
        public void Main_Refactor_Help_ShouldReturnSuccess()
        {
            // Arrange
            var args = new[] { "refactor", "--help" };

            // Act
            var exitCode = Program.Main(args);

            // Assert
            exitCode.Should().Be(0);
        }

        [Fact]
        public void Main_Refactor_NoArgs_ShouldReturnFailureCode2()
        {
            // Arrange
            var args = new[] { "refactor" };

            // Act
            var exitCode = Program.Main(args);

            // Assert
            exitCode.Should().Be(2);
        }

        [Fact]
        public void Main_Scan_WithMissingExplicitConfiguration_ShouldReturnFailureCode2()
        {
            File.WriteAllText(_tempPlanFile, "<ShowPlanXML />");
            string missingConfig = Path.Combine(
                Path.GetTempPath(),
                $"missing_config_{Guid.NewGuid():N}.json");
            var errorWriter = new StringWriter();
            TextWriter originalError = Console.Error;

            int exitCode;
            try
            {
                Console.SetError(errorWriter);
                exitCode = Program.Main(new[]
                {
                    "--path", _tempPlanFile,
                    "--config", missingConfig
                });
            }
            finally
            {
                Console.SetError(originalError);
            }

            exitCode.Should().Be(2);
            errorWriter.ToString().Should().Contain("规则配置文件不存在");
        }

        [Fact]
        public void Main_Refactor_NonExistentFile_ShouldReturnFailureCode1()
        {
            // Arrange
            var args = new[] { "refactor", "nonexistent.sql" };

            // Act
            var exitCode = Program.Main(args);

            // Assert
            exitCode.Should().Be(1);
        }

        [Fact]
        public void Main_Refactor_ValidSql_NoChanges_ShouldReturnSuccess()
        {
            // Arrange
            string originalSql = "SELECT * FROM Users WHERE Id = 1;";
            File.WriteAllText(_tempSqlFile, originalSql);
            var args = new[] { "refactor", _tempSqlFile };

            // Act
            var exitCode = Program.Main(args);

            // Assert
            exitCode.Should().Be(0);
            var content = File.ReadAllText(_tempSqlFile);
            content.Replace(" ", "").Replace("\r", "").Replace("\n", "").Trim()
                .Should().Be("SELECT*FROMUsersWHEREId=1;");
        }

        [Fact]
        public void Main_Refactor_ValidSql_WithConstantFolding_DryRun_ShouldNotModifyFile()
        {
            // Arrange
            string originalSql = "SELECT * FROM Users WHERE Age + 10 > 50;";
            File.WriteAllText(_tempSqlFile, originalSql);
            var args = new[] { "refactor", _tempSqlFile, "--dry-run" };

            // Act
            var exitCode = Program.Main(args);

            // Assert
            exitCode.Should().Be(0);
            File.ReadAllText(_tempSqlFile).Should().Be(originalSql); // File unchanged
        }

        [Fact]
        public void Main_Refactor_ValidSql_WithConstantFolding_WriteBack_ShouldModifyFile()
        {
            // Arrange
            string originalSql = "SELECT * FROM Users WHERE Age + 10 > 50;";
            File.WriteAllText(_tempSqlFile, originalSql);
            var args = new[] { "refactor", _tempSqlFile };

            // Act
            var exitCode = Program.Main(args);

            // Assert
            exitCode.Should().Be(0);
            var resultSql = File.ReadAllText(_tempSqlFile);
            resultSql.Should().NotBe(originalSql);
            resultSql.Should().Contain("Age > 40");
        }

        [Fact]
        public void Main_Refactor_JsonFormat_ShouldOutputValidJson()
        {
            // Arrange
            string originalSql = "SELECT * FROM Users WHERE Age + 10 > 50;";
            File.WriteAllText(_tempSqlFile, originalSql);
            var args = new[] { "refactor", _tempSqlFile, "--dry-run", "--format", "json" };

            // Act
            var stringWriter = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(stringWriter);

            int exitCode;
            try
            {
                exitCode = Program.Main(args);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            // Assert
            exitCode.Should().Be(0);
            var output = stringWriter.ToString();
            output.Should().NotBeNullOrEmpty();

            try
            {
                // Try parse output as JSON
                var doc = JsonDocument.Parse(output);
                doc.RootElement.GetProperty("IsSuccess").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("HasChanges").GetBoolean().Should().BeTrue();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse JSON. Output was: [{output}]", ex);
            }
        }

        [Fact]
        public void Main_Refactor_UnknownOption_ShouldReturnFailureCode2AndPrintErrorMessage()
        {
            // Arrange
            var args = new[] { "refactor", _tempSqlFile, "--unknown-flag" };

            // Act
            var errorWriter = new StringWriter();
            var originalError = Console.Error;
            Console.SetError(errorWriter);
            int exitCode;
            try
            {
                exitCode = Program.Main(args);
            }
            finally
            {
                Console.SetError(originalError);
            }

            // Assert
            exitCode.Should().Be(2);
            errorWriter.ToString().Should().Contain("[Error] 参数错误: 未知的选项或参数 '--unknown-flag'。");
        }

        [Fact]
        public void Main_Refactor_MissingOptionValue_ShouldReturnFailureCode2AndPrintErrorMessage()
        {
            // Arrange
            var args = new[] { "refactor", _tempSqlFile, "--plan" };

            // Act
            var errorWriter = new StringWriter();
            var originalError = Console.Error;
            Console.SetError(errorWriter);
            int exitCode;
            try
            {
                exitCode = Program.Main(args);
            }
            finally
            {
                Console.SetError(originalError);
            }

            // Assert
            exitCode.Should().Be(2);
            errorWriter.ToString().Should().Contain("[Error] 参数错误: 选项 '--plan' 缺少参数值。");
        }

        [Fact]
        public void Main_Refactor_InvalidMaxPasses_ShouldReturnFailureCode2AndPrintErrorMessage()
        {
            // Arrange
            var args = new[] { "refactor", _tempSqlFile, "--max-passes", "abc" };

            // Act
            var errorWriter = new StringWriter();
            var originalError = Console.Error;
            Console.SetError(errorWriter);
            int exitCode;
            try
            {
                exitCode = Program.Main(args);
            }
            finally
            {
                Console.SetError(originalError);
            }

            // Assert
            exitCode.Should().Be(2);
            errorWriter.ToString().Should().Contain("[Error] 参数错误: 选项 '--max-passes' 的值必须是大于 0 的有效整数");
        }

        [Fact]
        public void Main_Refactor_InvalidFormat_ShouldReturnFailureCode2AndPrintErrorMessage()
        {
            // Arrange
            var args = new[] { "refactor", _tempSqlFile, "--format", "xml" };

            // Act
            var errorWriter = new StringWriter();
            var originalError = Console.Error;
            Console.SetError(errorWriter);
            int exitCode;
            try
            {
                exitCode = Program.Main(args);
            }
            finally
            {
                Console.SetError(originalError);
            }

            // Assert
            exitCode.Should().Be(2);
            errorWriter.ToString().Should().Contain("[Error] 参数错误: 不支持的输出格式 'xml'。支持的格式为: console, json。");
        }

        [Fact]
        public void Main_Refactor_SqlPathStartsWithDash_ShouldReturnFailureCode2AndPrintErrorMessage()
        {
            // Arrange
            var args = new[] { "refactor", "--dry-run" }; // First arg starts with '-'

            // Act
            var errorWriter = new StringWriter();
            var originalError = Console.Error;
            Console.SetError(errorWriter);
            int exitCode;
            try
            {
                exitCode = Program.Main(args);
            }
            finally
            {
                Console.SetError(originalError);
            }

            // Assert
            exitCode.Should().Be(2);
            errorWriter.ToString().Should().Contain("[Error] 参数错误: SQL 文件路径不能以 '-' 开头: '--dry-run'。");
        }
    }
}
