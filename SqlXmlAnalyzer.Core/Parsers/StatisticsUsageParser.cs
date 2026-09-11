using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Parsers
{
    public static class StatisticsUsageParser
    {
        public static List<StatisticsInfo> Parse(XDocument doc, XNamespace ns)
        {
            if (doc?.Root == null) return new List<StatisticsInfo>();
            return QueryPlanXml.Enumerate(doc, ns).SelectMany(query => ParseQueryPlan(query, ns)).ToList();
        }

        public static List<StatisticsInfo> ParseQueryPlan(XElement queryPlan, XNamespace ns)
        {
            var list = new List<StatisticsInfo>();
            if (queryPlan.Name != ns + "QueryPlan" || !QueryPlanXml.IsInScope(queryPlan, ns)) return list;

            foreach (var elem in queryPlan.Elements(ns + "OptimizerStatsUsage").Elements(ns + "StatisticsInfo"))
            {
                var info = new StatisticsInfo
                {
                    Database = elem.Attribute("Database")?.Value ?? string.Empty,
                    Schema = elem.Attribute("Schema")?.Value ?? string.Empty,
                    Table = elem.Attribute("Table")?.Value ?? string.Empty,
                    Statistics = elem.Attribute("Statistics")?.Value ?? string.Empty
                };

                string? lastUpdateStr = elem.Attribute("LastUpdate")?.Value;
                if (!string.IsNullOrEmpty(lastUpdateStr) && DateTime.TryParse(lastUpdateStr, System.Globalization.CultureInfo.InvariantCulture, out var parsedDate))
                {
                    info.LastUpdate = parsedDate;
                }

                if (long.TryParse(elem.Attribute("ModificationCount")?.Value, out long modCount))
                {
                    info.ModificationCount = modCount;
                }

                if (double.TryParse(elem.Attribute("SamplingPercent")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double samplePercent))
                {
                    info.SamplingPercent = samplePercent;
                }

                list.Add(info);
            }

            return list;
        }
    }
}
