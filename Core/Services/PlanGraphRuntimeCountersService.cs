using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanGraphRuntimeCountersResult(
        bool HasActual,
        bool HasActualRead,
        double ActualRows,
        double ActualRowsRead,
        double ActualExecutions,
        double ActualRebinds,
        double ActualRewinds,
        bool IsThreadDataSkewed);

    public sealed class PlanGraphRuntimeCountersService
    {
        public PlanGraphRuntimeCountersResult Parse(
            XElement relOp,
            XNamespace ns)
        {
            ArgumentNullException.ThrowIfNull(relOp);

            double actualRows = 0.0;
            double actualRowsRead = 0.0;
            double actualExecutions = 0.0;
            bool hasActual = false;
            bool hasActualRead = false;
            double actualRebinds = 0.0;
            double actualRewinds = 0.0;
            var threadRows = new Dictionary<string, double>();

            XElement? runInfo = relOp.Element(ns + "RunTimeInformation");
            if (runInfo != null)
            {
                hasActual = true;
                foreach (XElement runtimeCounter in runInfo.Elements(ns + "RunTimeCountersPerThread"))
                {
                    string threadId = runtimeCounter.Attribute("Thread")?.Value ?? "0";
                    double rows = ParseDouble(runtimeCounter.Attribute("ActualRows")?.Value);
                    double rowsRead = rows;

                    if (runtimeCounter.Attribute("ActualRowsRead") != null)
                    {
                        rowsRead = ParseDouble(runtimeCounter.Attribute("ActualRowsRead")?.Value);
                        hasActualRead = true;
                    }

                    double executions = ParseDouble(
                        runtimeCounter.Attribute("ActualExecutions")?.Value,
                        defaultValue: 1.0);

                    threadRows[threadId] = rows;
                    actualRows += rows;
                    actualRowsRead += rowsRead;
                    actualExecutions += executions;
                    actualRebinds += ParseDouble(runtimeCounter.Attribute("ActualRebinds")?.Value);
                    actualRewinds += ParseDouble(runtimeCounter.Attribute("ActualRewinds")?.Value);
                }
            }

            if (!hasActual)
            {
                actualExecutions = 0.0;
            }

            return new PlanGraphRuntimeCountersResult(
                hasActual,
                hasActualRead,
                actualRows,
                actualRowsRead,
                actualExecutions,
                actualRebinds,
                actualRewinds,
                IsThreadDataSkewed(threadRows));
        }

        private static bool IsThreadDataSkewed(
            IReadOnlyDictionary<string, double> threadRows)
        {
            List<double> workerRows = threadRows
                .Where(pair => pair.Key != "0")
                .Select(pair => pair.Value)
                .ToList();

            if (workerRows.Count <= 1 || workerRows.Sum() <= 100)
            {
                return false;
            }

            double averageRows = workerRows.Sum() / workerRows.Count;
            return workerRows.Max() > averageRows * 2.0;
        }

        private static double ParseDouble(
            string? value,
            double defaultValue = 0.0)
        {
            return double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double parsed)
                ? parsed
                : defaultValue;
        }
    }
}
