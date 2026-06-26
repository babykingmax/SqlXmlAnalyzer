using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record TuningSessionLoadResult(
        IReadOnlyList<PlanSnapshot> Snapshots,
        PlanSnapshot? PlanA,
        PlanSnapshot? PlanB);

    public sealed class TuningSessionService
    {
        private static readonly XNamespace FallbackShowplanNamespace =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        private static readonly XNamespace SessionNamespace =
            "http://schemas.sqlxmlanalyzer.com/session";

        public PlanSnapshot CaptureSnapshot(
            XDocument currentPlanDocument,
            string? currentPlanFilePath,
            int versionNumber,
            DateTime? captureTime = null)
        {
            if (currentPlanDocument == null)
            {
                throw new ArgumentNullException(nameof(currentPlanDocument));
            }

            XNamespace showplanNamespace = GetShowplanNamespace(currentPlanDocument);
            XElement? rootRelOp = currentPlanDocument
                .Descendants(showplanNamespace + "RelOp")
                .FirstOrDefault();
            double cost = ParseDouble(rootRelOp?.Attribute("EstimatedTotalSubtreeCost")?.Value);
            int operatorCount = currentPlanDocument
                .Descendants(showplanNamespace + "RelOp")
                .Count();
            int missingIndexCount = currentPlanDocument
                .Descendants(showplanNamespace + "MissingIndex")
                .Count();
            string statementText = currentPlanDocument
                .Descendants(showplanNamespace + "StmtSimple")
                .FirstOrDefault()
                ?.Attribute("StatementText")
                ?.Value
                ?? "Unable to extract SQL statement";

            string fileName = Path.GetFileName(
                string.IsNullOrWhiteSpace(currentPlanFilePath)
                    ? "Untitled"
                    : currentPlanFilePath);

            return new PlanSnapshot
            {
                Title = $"Plan version #{versionNumber} - {fileName}",
                FilePath = currentPlanFilePath ?? string.Empty,
                CaptureTime = captureTime ?? DateTime.Now,
                Document = new XDocument(currentPlanDocument),
                TotalCost = cost,
                OperatorCount = operatorCount,
                MissingIndexCount = missingIndexCount,
                StatementText = statementText
            };
        }

        public void Save(
            string filePath,
            IEnumerable<PlanSnapshot> snapshots,
            PlanSnapshot? planA,
            PlanSnapshot? planB)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be empty.", nameof(filePath));
            }

            var snapshotElements = snapshots.Select(snapshot =>
                new XElement(
                    SessionNamespace + "Snapshot",
                    new XAttribute("Id", snapshot.Id),
                    new XAttribute("Title", snapshot.Title),
                    new XAttribute("FilePath", snapshot.FilePath),
                    new XAttribute("CaptureTime", snapshot.CaptureTime.ToString("o", CultureInfo.InvariantCulture)),
                    new XAttribute("TotalCost", snapshot.TotalCost.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("OperatorCount", snapshot.OperatorCount),
                    new XAttribute("MissingIndexCount", snapshot.MissingIndexCount),
                    new XElement(SessionNamespace + "StatementText", snapshot.StatementText),
                    new XElement(
                        SessionNamespace + "PlanDoc",
                        snapshot.Document.Root == null
                            ? null
                            : new XElement(snapshot.Document.Root))));

            var root = new XElement(
                SessionNamespace + "TuningSession",
                new XAttribute("Version", "2.0"),
                new XAttribute("Created", DateTime.Now.ToString("o", CultureInfo.InvariantCulture)),
                new XElement(SessionNamespace + "Snapshots", snapshotElements));

            if (planA != null)
            {
                root.Add(new XAttribute("PlanAId", planA.Id));
            }

            if (planB != null)
            {
                root.Add(new XAttribute("PlanBId", planB.Id));
            }

            var document = new XDocument(root);
            document.Save(filePath);
        }

        public TuningSessionLoadResult Load(string filePath)
        {
            XDocument document = SafeXmlHelper.LoadSafe(filePath);
            if (document.Root == null)
            {
                return new TuningSessionLoadResult(
                    Array.Empty<PlanSnapshot>(),
                    null,
                    null);
            }

            XElement? snapshotsElement = document.Root.Element(SessionNamespace + "Snapshots");
            if (snapshotsElement == null)
            {
                return new TuningSessionLoadResult(
                    Array.Empty<PlanSnapshot>(),
                    null,
                    null);
            }

            var snapshots = new List<PlanSnapshot>();
            var snapshotsBySavedId = new Dictionary<string, PlanSnapshot>();

            foreach (XElement snapshotElement in snapshotsElement.Elements(SessionNamespace + "Snapshot"))
            {
                string savedId = snapshotElement.Attribute("Id")?.Value ?? Guid.NewGuid().ToString();
                PlanSnapshot snapshot = CreateSnapshotFromElement(snapshotElement);
                snapshots.Add(snapshot);
                snapshotsBySavedId[savedId] = snapshot;
            }

            string planAId = document.Root.Attribute("PlanAId")?.Value ?? string.Empty;
            string planBId = document.Root.Attribute("PlanBId")?.Value ?? string.Empty;
            snapshotsBySavedId.TryGetValue(planAId, out PlanSnapshot? planA);
            snapshotsBySavedId.TryGetValue(planBId, out PlanSnapshot? planB);

            return new TuningSessionLoadResult(
                snapshots,
                planA,
                planB);
        }

        private static PlanSnapshot CreateSnapshotFromElement(XElement snapshotElement)
        {
            string title = snapshotElement.Attribute("Title")?.Value ?? "Snapshot";
            string originalPath = snapshotElement.Attribute("FilePath")?.Value ?? string.Empty;
            DateTime.TryParse(
                snapshotElement.Attribute("CaptureTime")?.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime captureTime);
            double totalCost = ParseDouble(snapshotElement.Attribute("TotalCost")?.Value);
            int.TryParse(snapshotElement.Attribute("OperatorCount")?.Value, out int operatorCount);
            int.TryParse(snapshotElement.Attribute("MissingIndexCount")?.Value, out int missingIndexCount);
            string statementText = snapshotElement.Element(SessionNamespace + "StatementText")?.Value ?? string.Empty;

            XElement? planDocumentElement = snapshotElement
                .Element(SessionNamespace + "PlanDoc")
                ?.Elements()
                .FirstOrDefault();
            XDocument planDocument = planDocumentElement != null
                ? new XDocument(new XElement(planDocumentElement))
                : new XDocument();

            return new PlanSnapshot
            {
                Title = title,
                FilePath = originalPath,
                CaptureTime = captureTime == default ? DateTime.Now : captureTime,
                Document = planDocument,
                TotalCost = totalCost,
                OperatorCount = operatorCount,
                MissingIndexCount = missingIndexCount,
                StatementText = statementText
            };
        }

        private static XNamespace GetShowplanNamespace(XDocument document)
        {
            XNamespace defaultNamespace = document.Root?.GetDefaultNamespace() ?? XNamespace.None;
            if (!string.IsNullOrEmpty(defaultNamespace.NamespaceName))
            {
                return defaultNamespace;
            }

            XNamespace elementNamespace = document.Root?.Name.Namespace ?? XNamespace.None;
            return !string.IsNullOrEmpty(elementNamespace.NamespaceName)
                ? elementNamespace
                : FallbackShowplanNamespace;
        }

        private static double ParseDouble(string? value)
        {
            return double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double parsed)
                ? parsed
                : 0;
        }
    }
}
