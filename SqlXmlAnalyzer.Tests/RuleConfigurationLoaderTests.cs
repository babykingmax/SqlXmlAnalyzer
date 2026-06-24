using System;
using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Configuration;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class RuleConfigurationLoaderTests : IDisposable
    {
        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer_Config_{Guid.NewGuid():N}");

        public RuleConfigurationLoaderTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public void Resolve_WithoutExplicitPath_UsesApplicationBaseDirectory()
        {
            string resolvedPath = RuleConfigurationPathResolver.Resolve();

            resolvedPath.Should().Be(
                Path.Combine(AppContext.BaseDirectory, RuleConfigurationPathResolver.DefaultFileName));
        }

        [Fact]
        public void Load_ExplicitMissingPath_ReturnsFailure()
        {
            string path = Path.Combine(_tempDirectory, "missing.json");

            RuleConfigurationLoadResult result = RuleConfigurationLoader.Load(path);

            result.IsSuccess.Should().BeFalse();
            result.IsExplicitPath.Should().BeTrue();
            result.Errors.Should().ContainSingle(message => message.Contains("不存在"));
        }

        [Fact]
        public void Load_InvalidJson_ReturnsFailure()
        {
            string path = Path.Combine(_tempDirectory, "invalid.json");
            File.WriteAllText(path, "{ invalid json");

            RuleConfigurationLoadResult result = RuleConfigurationLoader.Load(path);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainSingle(message => message.Contains("JSON"));
        }

        [Fact]
        public void Load_DuplicateRuleIdAndInvalidSeverity_ReturnsValidationErrors()
        {
            string path = Path.Combine(_tempDirectory, "invalid-rules.json");
            File.WriteAllText(path, """
                {
                  "Rules": [
                    { "RuleId": "RULE_TEST", "Enabled": true },
                    { "RuleId": "rule_test", "Enabled": true, "SeverityOverride": "Severe" },
                    { "RuleId": "", "Enabled": true }
                  ]
                }
                """);

            RuleConfigurationLoadResult result = RuleConfigurationLoader.Load(path);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(message => message.Contains("重复 RuleId"));
            result.Errors.Should().Contain(message => message.Contains("SeverityOverride"));
            result.Errors.Should().Contain(message => message.Contains("RuleId 不能为空"));
        }

        [Fact]
        public void Load_ValidExplicitConfiguration_ReturnsNormalizedConfiguration()
        {
            string path = Path.Combine(_tempDirectory, "valid.json");
            File.WriteAllText(path, """
                {
                  "Rules": [
                    { "RuleId": " RULE_TEST ", "Enabled": false, "SeverityOverride": " warning " }
                  ]
                }
                """);

            RuleConfigurationLoadResult result = RuleConfigurationLoader.Load(path);

            result.IsSuccess.Should().BeTrue();
            result.ResolvedPath.Should().Be(Path.GetFullPath(path));
            result.Configuration.Rules.Should().ContainSingle();
            result.Configuration.Rules[0].RuleId.Should().Be("RULE_TEST");
            result.Configuration.Rules[0].SeverityOverride.Should().Be("Warning");
        }
    }
}
