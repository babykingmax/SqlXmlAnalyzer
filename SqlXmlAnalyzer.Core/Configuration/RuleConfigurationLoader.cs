using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
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
        public List<RuleConfig> Rules { get; set; } = new List<RuleConfig>();
    }

    public static class RuleConfigurationLoader
    {
        public static RuleConfigurationRoot Load(string configPath)
        {
            if (!File.Exists(configPath))
            {
                return new RuleConfigurationRoot(); // Default empty config
            }

            try
            {
                string json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<RuleConfigurationRoot>(json) ?? new RuleConfigurationRoot();
            }
            catch (System.Exception ex)
            {
                Logger.LogException("Failed to load RuleConfiguration.json", ex);
                return new RuleConfigurationRoot();
            }
        }
    }
}
