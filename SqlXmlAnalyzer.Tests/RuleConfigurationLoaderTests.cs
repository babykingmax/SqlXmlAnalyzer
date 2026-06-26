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
            result.Errors.Should().ContainSingle(message => message.Contains("does not exist"));
        }

        [Fact]
        public void Load_InvalidJson_ReturnsFailure()
        {
            string path = Path.Combine(_tempDirectory, "invalid.json");
            File.WriteAllText(path, "{ invalid json");

            RuleConfigurationLoadResult result = RuleConfigurationLoader.Load(path);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainSingle(message => message.Contains("invalid JSON"));
        }

        [Fact]
        public void Load_DuplicateRuleIdAndInvalidSeverity_ReturnsValidationErrors()
        {
            string path = Path.Combine(_tempDirectory, "invalid-rules.json");
            File.WriteAllText(path, """
                {
                  "Rules": [
                    { "RuleId": "RULE_001_IMPLICIT_CONV", "Enabled": true },
                    { "RuleId": "rule_001_implicit_conv", "Enabled": true, "SeverityOverride": "Severe" },
                    { "RuleId": "", "Enabled": true }
                  ]
                }
                """);

            RuleConfigurationLoadResult result = RuleConfigurationLoader.Load(path);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(message => message.Contains("Duplicate RuleId"));
            result.Errors.Should().Contain(message => message.Contains("SeverityOverride"));
            result.Errors.Should().Contain(message => message.Contains("RuleId cannot be empty"));
        }

        [Fact]
        public void Load_UnknownRuleId_ReturnsValidationError()
        {
            string path = Path.Combine(_tempDirectory, "unknown-rule.json");
            File.WriteAllText(path, """
                {
                  "Rules": [
                    { "RuleId": "RULE_DOES_NOT_EXIST", "Enabled": true }
                  ]
                }
                """);

            RuleConfigurationLoadResult result = RuleConfigurationLoader.Load(path);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainSingle(message =>
                message.Contains("Unknown RuleId") &&
                message.Contains("RULE_DOES_NOT_EXIST"));
        }

        [Fact]
        public void Load_DeprecatedRuleIdAlias_NormalizesAndWarns()
        {
            string path = Path.Combine(_tempDirectory, "legacy-rule.json");
            File.WriteAllText(path, """
                {
                  "Rules": [
                    { "RuleId": "RULE_016_WAIT_STATS", "Enabled": false }
                  ]
                }
                """);

            RuleConfigurationLoadResult result = RuleConfigurationLoader.Load(path);

            result.IsSuccess.Should().BeTrue();
            result.Configuration.Rules.Should().ContainSingle();
            result.Configuration.Rules[0].RuleId.Should().Be("RULE_036_WAIT_STATS");
            result.Warnings.Should().ContainSingle(message =>
                message.Contains("deprecated") &&
                message.Contains("RULE_036_WAIT_STATS"));
        }

        [Fact]
        public void Load_ValidExplicitConfiguration_ReturnsNormalizedConfiguration()
        {
            string path = Path.Combine(_tempDirectory, "valid.json");
            File.WriteAllText(path, """
                {
                  "Rules": [
                    { "RuleId": " RULE_001_IMPLICIT_CONV ", "Enabled": false, "SeverityOverride": " warning " }
                  ]
                }
                """);

            RuleConfigurationLoadResult result = RuleConfigurationLoader.Load(path);

            result.IsSuccess.Should().BeTrue();
            result.ResolvedPath.Should().Be(Path.GetFullPath(path));
            result.Configuration.Rules.Should().ContainSingle();
            result.Configuration.Rules[0].RuleId.Should().Be("RULE_001_IMPLICIT_CONV");
            result.Configuration.Rules[0].SeverityOverride.Should().Be("Warning");
        }
    }
}
