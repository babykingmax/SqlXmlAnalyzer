using System;
using System.Collections.Generic;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Configuration;

namespace SqlXmlAnalyzer.Analysis
{
    public class SqlXmlAnalysisEngine : IAnalysisEngine
    {
        private readonly string? _configPath;

        public SqlXmlAnalysisEngine(string? configPath = null)
        {
            _configPath = string.IsNullOrWhiteSpace(configPath)
                ? null
                : RuleConfigurationPathResolver.Resolve(configPath);
        }

        public AnalysisReport Analyze(string xmlContent)
        {
            if (string.IsNullOrWhiteSpace(xmlContent))
            {
                return new AnalysisReport(new List<IAnalysisIssue>());
            }

            try
            {
                var doc = SqlXmlAnalyzer.SafeXmlHelper.ParseSafe(xmlContent);
                if (doc.Root == null)
                {
                    return new AnalysisReport(new List<IAnalysisIssue>());
                }

                XNamespace ns = doc.Root.GetDefaultNamespace();
                if (string.IsNullOrEmpty(ns.NamespaceName))
                {
                    ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
                }

                var ruleResults = PlanDiagnosticAnalyzer.AnalyzePlan(doc, ns, _configPath);

                var issues = new List<IAnalysisIssue>();
                foreach (var res in ruleResults)
                {
                    var severity = MapSeverity(res.Severity);

                    issues.Add(new SqlPlanAnalysisIssue(
                        res.RuleId,
                        $"[Node {res.NodeId}] {res.Title}: {res.Message}",
                        severity
                    ));
                }

                return new AnalysisReport(issues);
            }
            catch (Exception ex)
            {
                var failureIssue = new SqlPlanAnalysisIssue(
                    "PARSE_ERROR",
                    $"Failed to parse execution plan: {ex.Message}",
                    IssueSeverity.Critical
                );
                return new AnalysisReport(new List<IAnalysisIssue> { failureIssue });
            }
        }

        private static IssueSeverity MapSeverity(string severityStr)
        {
            switch (severityStr?.Trim())
            {
                case "Critical":
                    return IssueSeverity.Critical;
                case "Info":
                    return IssueSeverity.Info;
                case "Warning":
                default:
                    return IssueSeverity.Warning;
            }
        }
    }
}
