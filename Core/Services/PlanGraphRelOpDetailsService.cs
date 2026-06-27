using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphRelOpDetails(
        string ObjectDetails,
        string DatabaseName,
        string TableName,
        string IndexName,
        IReadOnlyList<string> Predicates,
        IReadOnlyList<string> SeekPredicates,
        IReadOnlyList<string> OutputColumns,
        bool IsPartitioned,
        string PartitionCount,
        string PartitionRange);

    public sealed class PlanGraphRelOpDetailsService
    {
        public PlanGraphRelOpDetails Parse(
            XElement relOp,
            XNamespace ns,
            string? physicalOp)
        {
            ArgumentNullException.ThrowIfNull(relOp);

            string objectDetails = string.Empty;
            string databaseName = string.Empty;
            string tableName = string.Empty;
            string indexName = string.Empty;
            var predicates = new List<string>();
            var seekPredicates = new List<string>();
            var outputColumns = new List<string>();

            foreach (XElement child in relOp.Elements())
            {
                string tagLocal = child.Name.LocalName;
                if (tagLocal == "OutputList")
                {
                    AddOutputColumns(child, ns, outputColumns);
                    continue;
                }

                if (tagLocal == "Warnings"
                    || tagLocal == "RunTimeInformation"
                    || tagLocal == "RelOp")
                {
                    continue;
                }

                XElement? obj = child.Descendants(ns + "Object").FirstOrDefault();
                if (obj != null)
                {
                    databaseName = TrimSqlName(obj.Attribute("Database")?.Value);
                    tableName = TrimSqlName(obj.Attribute("Table")?.Value);
                    indexName = TrimSqlName(obj.Attribute("Index")?.Value);
                    string alias = TrimSqlName(obj.Attribute("Alias")?.Value);

                    if (!string.IsNullOrEmpty(tableName))
                    {
                        objectDetails = string.IsNullOrEmpty(indexName)
                            ? $"[{tableName}]"
                            : $"[{tableName}].[{indexName}]";

                        if (!string.IsNullOrEmpty(alias) && alias != tableName)
                        {
                            objectDetails += $" AS [{alias}]";
                        }
                    }
                }

                AddPredicateScalars(child, ns, predicates);
                AddSeekPredicateScalars(child, ns, seekPredicates);
            }

            if (string.IsNullOrEmpty(objectDetails)
                && (physicalOp?.Contains("Scan") == true
                    || physicalOp?.Contains("Seek") == true))
            {
                objectDetails = "(堆表或堆索引)";
            }

            (bool isPartitioned, string partitionCount, string partitionRange) =
                ParsePartitionInfo(relOp, ns);

            return new PlanGraphRelOpDetails(
                objectDetails,
                databaseName,
                tableName,
                indexName,
                predicates,
                seekPredicates,
                outputColumns,
                isPartitioned,
                partitionCount,
                partitionRange);
        }

        private static void AddOutputColumns(
            XElement outputListElement,
            XNamespace ns,
            ICollection<string> outputColumns)
        {
            foreach (XElement column in outputListElement.Descendants(ns + "ColumnReference"))
            {
                string columnName = column.Attribute("Column")?.Value ?? string.Empty;
                if (!string.IsNullOrEmpty(columnName) && !outputColumns.Contains(columnName))
                {
                    outputColumns.Add(columnName);
                }
            }
        }

        private static void AddPredicateScalars(
            XElement element,
            XNamespace ns,
            ICollection<string> predicates)
        {
            foreach (XElement predicate in element.Descendants(ns + "Predicate"))
            {
                foreach (XElement scalarOperator in predicate.Descendants(ns + "ScalarOperator"))
                {
                    string? scalar = scalarOperator.Attribute("ScalarString")?.Value;
                    if (!string.IsNullOrEmpty(scalar) && !predicates.Contains(scalar))
                    {
                        predicates.Add(scalar);
                    }
                }
            }
        }

        private static void AddSeekPredicateScalars(
            XElement element,
            XNamespace ns,
            ICollection<string> seekPredicates)
        {
            IEnumerable<XElement> seekPredicateElements =
                element.Descendants(ns + "SeekPredicates")
                    .Concat(element.Descendants(ns + "SeekPredicateNew"));

            foreach (XElement seekPredicate in seekPredicateElements)
            {
                foreach (XElement scalarOperator in seekPredicate.Descendants(ns + "ScalarOperator"))
                {
                    string? scalar = scalarOperator.Attribute("ScalarString")?.Value;
                    if (!string.IsNullOrEmpty(scalar) && !seekPredicates.Contains(scalar))
                    {
                        seekPredicates.Add(scalar);
                    }
                }
            }
        }

        private static (bool IsPartitioned, string PartitionCount, string PartitionRange)
            ParsePartitionInfo(
                XElement relOp,
                XNamespace ns)
        {
            XElement? partitionsAccessed =
                relOp.Descendants(ns + "PartitionsAccessed").FirstOrDefault();
            string partitionCount = string.Empty;
            string partitionRange = string.Empty;
            bool isPartitioned = relOp.DescendantsAndSelf()
                .Any(element =>
                    element.Attribute("Partitioned")?.Value?.ToLower() == "true"
                    || element.Attribute("Partitioned")?.Value == "1");

            if (partitionsAccessed != null)
            {
                isPartitioned = true;
                partitionCount = partitionsAccessed.Attribute("PartitionCount")?.Value ?? string.Empty;
                XElement? range = partitionsAccessed.Element(ns + "PartitionRange");
                if (range != null)
                {
                    string start = range.Attribute("Start")?.Value ?? string.Empty;
                    string end = range.Attribute("End")?.Value ?? string.Empty;
                    if (!string.IsNullOrEmpty(start) && !string.IsNullOrEmpty(end))
                    {
                        partitionRange = $"{start} - {end}";
                    }
                    else if (!string.IsNullOrEmpty(start))
                    {
                        partitionRange = start;
                    }
                }
            }

            return (isPartitioned, partitionCount, partitionRange);
        }

        private static string TrimSqlName(string? value)
        {
            return value?.TrimStart('[').TrimEnd(']') ?? string.Empty;
        }
    }
}
