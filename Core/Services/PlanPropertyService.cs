using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanPropertyItem(
        string Group,
        string Name,
        string Value);

    public sealed class PlanPropertyService
    {
        private static readonly IReadOnlyDictionary<string, (string Group, string Name)> AttributeMap =
            new Dictionary<string, (string Group, string Name)>(StringComparer.Ordinal)
            {
                ["NodeId"] = ("Operator", "Node ID"),
                ["PhysicalOp"] = ("Operator", "Physical Operator"),
                ["LogicalOp"] = ("Operator", "Logical Operator"),
                ["Parallel"] = ("Operator", "Parallel"),
                ["EstimatedExecutionMode"] = ("Operator", "Estimated Execution Mode"),
                ["ActualExecutionMode"] = ("Runtime", "Actual Execution Mode"),
                ["EstimateRows"] = ("Estimates", "Estimated Rows"),
                ["EstimatedRowsRead"] = ("Estimates", "Estimated Rows Read"),
                ["EstimateIO"] = ("Estimates", "Estimated I/O Cost"),
                ["EstimateCPU"] = ("Estimates", "Estimated CPU Cost"),
                ["AvgRowSize"] = ("Estimates", "Average Row Size"),
                ["EstimatedTotalSubtreeCost"] = ("Estimates", "Estimated Subtree Cost"),
                ["EstimateRebinds"] = ("Estimates", "Estimated Rebinds"),
                ["EstimateRewinds"] = ("Estimates", "Estimated Rewinds"),
                ["TableCardinality"] = ("Estimates", "Table Cardinality")
            };

        private static readonly IReadOnlyDictionary<string, string> RuntimeAttributeNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ActualRows"] = "Actual Rows",
                ["ActualRowsRead"] = "Actual Rows Read",
                ["ActualExecutions"] = "Actual Executions",
                ["ActualElapsedms"] = "Actual Elapsed (ms)",
                ["ActualCPUms"] = "Actual CPU (ms)",
                ["ActualLogicalReads"] = "Actual Logical Reads",
                ["ActualPhysicalReads"] = "Actual Physical Reads",
                ["ActualEndOfScans"] = "Actual End Of Scans",
                ["ActualRebinds"] = "Actual Rebinds",
                ["ActualRewinds"] = "Actual Rewinds"
            };

        public IReadOnlyList<PlanPropertyItem> BuildProperties(XElement relOp)
        {
            if (relOp == null)
            {
                throw new ArgumentNullException(nameof(relOp));
            }

            var properties = new List<PlanPropertyItem>();

            foreach (XAttribute attribute in relOp.Attributes())
            {
                properties.Add(BuildAttributeProperty(attribute));
            }

            foreach (XElement child in relOp.Elements())
            {
                AddChildProperties(properties, child);
            }

            return properties;
        }

        private static PlanPropertyItem BuildAttributeProperty(XAttribute attribute)
        {
            string key = attribute.Name.LocalName;
            if (AttributeMap.TryGetValue(key, out (string Group, string Name) translation))
            {
                return new PlanPropertyItem(
                    translation.Group,
                    translation.Name,
                    attribute.Value);
            }

            return new PlanPropertyItem(
                "Misc",
                key,
                attribute.Value);
        }

        private static void AddChildProperties(
            List<PlanPropertyItem> properties,
            XElement child)
        {
            switch (child.Name.LocalName)
            {
                case "OutputList":
                    AddOutputListProperties(properties, child);
                    break;
                case "RunTimeInformation":
                    AddRuntimeProperties(properties, child);
                    break;
                case "RelOp":
                    break;
                default:
                    AddElementAttributeProperties(properties, child);
                    break;
            }
        }

        private static void AddOutputListProperties(
            List<PlanPropertyItem> properties,
            XElement outputList)
        {
            foreach (XElement column in outputList.Descendants(outputList.Name.Namespace + "ColumnReference"))
            {
                string name = FormatColumnReference(column);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    properties.Add(new PlanPropertyItem(
                        "Output List",
                        name,
                        string.Empty));
                }
            }
        }

        private static void AddRuntimeProperties(
            List<PlanPropertyItem> properties,
            XElement runtimeInformation)
        {
            foreach (XElement runtimeCounter in runtimeInformation.Elements())
            {
                foreach (XAttribute attribute in runtimeCounter.Attributes())
                {
                    string name = RuntimeAttributeNames.TryGetValue(attribute.Name.LocalName, out string? displayName)
                        ? displayName
                        : attribute.Name.LocalName;

                    properties.Add(new PlanPropertyItem(
                        "Runtime",
                        name,
                        attribute.Value));
                }
            }
        }

        private static void AddElementAttributeProperties(
            List<PlanPropertyItem> properties,
            XElement element)
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                properties.Add(new PlanPropertyItem(
                    element.Name.LocalName,
                    attribute.Name.LocalName,
                    attribute.Value));
            }
        }

        private static string FormatColumnReference(XElement column)
        {
            string? database = NormalizeIdentifier(column.Attribute("Database")?.Value);
            string? schema = NormalizeIdentifier(column.Attribute("Schema")?.Value);
            string? table = NormalizeIdentifier(column.Attribute("Table")?.Value);
            string? columnName = NormalizeIdentifier(column.Attribute("Column")?.Value);

            var ownerParts = new[] { database, schema, table }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();

            if (string.IsNullOrWhiteSpace(columnName))
            {
                return string.Join(".", ownerParts);
            }

            return ownerParts.Count == 0
                ? columnName
                : $"{string.Join(".", ownerParts)}.{columnName}";
        }

        private static string? NormalizeIdentifier(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
