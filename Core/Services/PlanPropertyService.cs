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
            var operatorFacts = PlanOperatorFactsService.Get(relOp, relOp.Name.Namespace);
            PlanExecutionFacts facts = operatorFacts.Execution;
            PlanGraphRuntimeCountersResult counters = new PlanGraphRuntimeCountersService().Parse(relOp, relOp.Name.Namespace);
            properties.Add(new("Operator", "并行 (Parallel)", facts.ParallelDisplay));
            properties.Add(new("Operator", "有序扫描 (Ordered)", facts.OrderedDisplay));
            properties.Add(new("Runtime", "实际执行模式", facts.ActualExecutionMode ?? "N/A"));
            properties.Add(new("Estimates", "估算执行模式", facts.EstimatedExecutionMode ?? "N/A"));
            properties.Add(new("Runtime", "实际输出行（行）", counters.ActualRowsDisplay));
            properties.Add(new("Runtime", "实际读取行（行）", counters.ActualRowsReadDisplay));
            properties.Add(new("Runtime", "线程执行次数合计（次）", operatorFacts.ThreadExecutions.Display("N0")));
            properties.Add(new("Runtime", "逻辑执行次数（次）", operatorFacts.LogicalExecutions.Display("N0")));
            properties.Add(new("Runtime", "每次执行输出行（行/次）", operatorFacts.RowsPerExecution.Display("N2")));
            properties.Add(new("Estimates", "Estimated Own Cost", operatorFacts.OwnCost.Display()));
            properties.Add(new("Predicate", "Residual Predicate", string.Join(" AND ", operatorFacts.Predicates)));
            properties.Add(new("Predicate", "Seek Predicate", string.Join(" AND ", operatorFacts.SeekPredicates)));

            var estimates = new Dictionary<string, Models.PlanMetric<double>>
            {
                ["EstimateRows"] = operatorFacts.EstimatedRows,
                ["EstimatedRowsRead"] = operatorFacts.EstimatedRowsRead,
                ["EstimateCPU"] = operatorFacts.EstimatedCpuCost,
                ["EstimateIO"] = operatorFacts.EstimatedIoCost,
                ["AvgRowSize"] = operatorFacts.AverageRowSize,
                ["EstimatedTotalSubtreeCost"] = operatorFacts.SubtreeCost
            };
            foreach (var pair in estimates)
                properties.Add(new("Estimates", AttributeMap[pair.Key].Name, pair.Value.Display()));

            foreach (XAttribute attribute in relOp.Attributes())
            {
                if (attribute.Name.LocalName is "Parallel" or "Ordered" or "EstimatedExecutionMode" or "ActualExecutionMode") continue;
                if (estimates.ContainsKey(attribute.Name.LocalName)) continue;
                properties.Add(BuildAttributeProperty(attribute));
            }

            foreach (XElement child in relOp.Elements().Where(child => child.Name.Namespace == relOp.Name.Namespace))
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
            foreach (XElement runtimeCounter in runtimeInformation.Elements(runtimeInformation.Name.Namespace + "RunTimeCountersPerThread"))
            {
                foreach (XAttribute attribute in runtimeCounter.Attributes())
                {
                    // Aggregate rows and mode above use the same reader as graph/table; keep other raw counters below.
                    if (attribute.Name.LocalName is "ActualRows" or "ActualRowsRead" or "ActualExecutionMode") continue;
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
                if (attribute.Name.LocalName == "Ordered") continue;
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
