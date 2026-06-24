using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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
                    $"规则配置路径无效: {ex.Message}");
            }

            if (!File.Exists(resolvedPath))
            {
                string message = $"规则配置文件不存在: {resolvedPath}";
                if (isExplicitPath)
                {
                    return Failure(resolvedPath, true, message);
                }

                return new RuleConfigurationLoadResult(
                    new RuleConfigurationRoot(),
                    resolvedPath,
                    new[] { $"{message}。将使用内置默认规则设置。" },
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
                    return Failure(resolvedPath, isExplicitPath, "规则配置文件内容为空。");
                }

                configuration.Rules ??= new List<RuleConfig>();
                var errors = Validate(configuration);
                if (errors.Count > 0)
                {
                    return new RuleConfigurationLoadResult(
                        new RuleConfigurationRoot(),
                        resolvedPath,
                        Array.Empty<string>(),
                        errors,
                        isExplicitPath);
                }

                return new RuleConfigurationLoadResult(
                    configuration,
                    resolvedPath,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    isExplicitPath);
            }
            catch (Exception ex)
            {
                Logger.LogException("RuleConfigurationLoader.Load", ex);
                return Failure(
                    resolvedPath,
                    isExplicitPath,
                    $"规则配置文件无法读取或 JSON 无效: {ex.Message}");
            }
        }

        private static List<string> Validate(RuleConfigurationRoot configuration)
        {
            var errors = new List<string>();
            var seenRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < configuration.Rules.Count; i++)
            {
                RuleConfig? rule = configuration.Rules[i];
                if (rule == null)
                {
                    errors.Add($"Rules[{i}] 不能为空。");
                    continue;
                }

                rule.RuleId = rule.RuleId?.Trim() ?? string.Empty;
                if (rule.RuleId.Length == 0)
                {
                    errors.Add($"Rules[{i}].RuleId 不能为空。");
                }
                else if (!seenRuleIds.Add(rule.RuleId))
                {
                    errors.Add($"存在重复 RuleId: {rule.RuleId}");
                }

                if (!string.IsNullOrWhiteSpace(rule.SeverityOverride))
                {
                    rule.SeverityOverride = rule.SeverityOverride.Trim();
                    if (!ValidSeverities.Contains(rule.SeverityOverride))
                    {
                        errors.Add(
                            $"规则 {rule.RuleId} 的 SeverityOverride 无效: {rule.SeverityOverride}。允许值为 Info、Warning、Critical。");
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

            return errors;
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
