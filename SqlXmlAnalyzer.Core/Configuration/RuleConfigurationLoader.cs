using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SqlXmlAnalyzer.Core.Rules;

namespace SqlXmlAnalyzer.Core.Configuration
{
    public class RuleConfig
    {
        public string RuleId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string? SeverityOverride { get; set; }
    }

    public class RuleConfigurationRoot
    {
        public List<RuleConfig> Rules { get; set; } = new();
    }

    public sealed record RuleConfigurationLoadResult(
        RuleConfigurationRoot Configuration,
        string ResolvedPath,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Errors,
        bool IsExplicitPath)
    {
        public bool IsSuccess => Errors.Count == 0;
    }

    public static class RuleConfigurationPathResolver
    {
        public const string DefaultFileName = "RuleConfiguration.json";

        public static string Resolve(string? configPath = null)
        {
            return string.IsNullOrWhiteSpace(configPath)
                ? Path.Combine(AppContext.BaseDirectory, DefaultFileName)
                : Path.GetFullPath(configPath);
        }
    }

    public static class RuleConfigurationLoader
    {
        private static readonly HashSet<string> ValidSeverities = new(
            new[] { "Info", "Warning", "Critical" },
            StringComparer.OrdinalIgnoreCase);

        public static RuleConfigurationLoadResult Load(string? configPath = null)
        {
            bool isExplicitPath = !string.IsNullOrWhiteSpace(configPath);
            string resolvedPath;

            try
            {
                resolvedPath = RuleConfigurationPathResolver.Resolve(configPath);
            }
            catch (Exception ex)
            {
                return Failure(
                    configPath ?? string.Empty,
                    isExplicitPath,
                    $"Rule configuration path is invalid: {ex.Message}");
            }

            if (!File.Exists(resolvedPath))
            {
                string message =
                    $"Rule configuration file does not exist: {resolvedPath} " +
                    "(规则配置文件不存在; 瑙勫垯閰嶇疆鏂囦欢涓嶅瓨鍦?)";
                if (isExplicitPath)
                {
                    return Failure(resolvedPath, true, message);
                }

                return new RuleConfigurationLoadResult(
                    new RuleConfigurationRoot(),
                    resolvedPath,
                    new[] { $"{message}. Built-in default rule settings will be used." },
                    Array.Empty<string>(),
                    false);
            }

            try
            {
                string json = File.ReadAllText(resolvedPath);
                var configuration = JsonSerializer.Deserialize<RuleConfigurationRoot>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (configuration == null)
                {
                    return Failure(resolvedPath, isExplicitPath, "Rule configuration file is empty.");
                }

                configuration.Rules ??= new List<RuleConfig>();
                var (warnings, errors) = Validate(configuration);
                if (errors.Count > 0)
                {
                    return new RuleConfigurationLoadResult(
                        new RuleConfigurationRoot(),
                        resolvedPath,
                        warnings,
                        errors,
                        isExplicitPath);
                }

                return new RuleConfigurationLoadResult(
                    configuration,
                    resolvedPath,
                    warnings,
                    Array.Empty<string>(),
                    isExplicitPath);
            }
            catch (Exception ex)
            {
                Logger.LogException("RuleConfigurationLoader.Load", ex);
                return Failure(
                    resolvedPath,
                    isExplicitPath,
                    $"Rule configuration file cannot be read or contains invalid JSON: {ex.Message}");
            }
        }

        private static (List<string> Warnings, List<string> Errors) Validate(
            RuleConfigurationRoot configuration)
        {
            var warnings = new List<string>();
            var errors = new List<string>();
            var seenRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < configuration.Rules.Count; i++)
            {
                RuleConfig? rule = configuration.Rules[i];
                if (rule == null)
                {
                    errors.Add($"Rules[{i}] cannot be null.");
                    continue;
                }

                rule.RuleId = rule.RuleId?.Trim() ?? string.Empty;
                if (rule.RuleId.Length == 0)
                {
                    errors.Add($"Rules[{i}].RuleId cannot be empty.");
                }
                else if (!RuleMetadataCatalog.TryNormalizeRuleId(
                             rule.RuleId,
                             out string normalizedRuleId,
                             out string? warning))
                {
                    errors.Add($"Unknown RuleId: {rule.RuleId}");
                }
                else
                {
                    if (warning != null)
                    {
                        warnings.Add(warning);
                    }

                    rule.RuleId = normalizedRuleId;
                    if (!seenRuleIds.Add(rule.RuleId))
                    {
                        errors.Add($"Duplicate RuleId: {rule.RuleId}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(rule.SeverityOverride))
                {
                    rule.SeverityOverride = rule.SeverityOverride.Trim();
                    if (!ValidSeverities.Contains(rule.SeverityOverride))
                    {
                        errors.Add(
                            $"Rule {rule.RuleId} has invalid SeverityOverride: {rule.SeverityOverride}. " +
                            "Allowed values are Info, Warning, Critical.");
                    }
                    else
                    {
                        rule.SeverityOverride = rule.SeverityOverride.ToUpperInvariant() switch
                        {
                            "INFO" => "Info",
                            "CRITICAL" => "Critical",
                            _ => "Warning"
                        };
                    }
                }
                else
                {
                    rule.SeverityOverride = null;
                }
            }

            return (warnings, errors);
        }

        private static RuleConfigurationLoadResult Failure(
            string resolvedPath,
            bool isExplicitPath,
            string error)
        {
            return new RuleConfigurationLoadResult(
                new RuleConfigurationRoot(),
                resolvedPath,
                Array.Empty<string>(),
                new[] { error },
                isExplicitPath);
        }
    }
}
