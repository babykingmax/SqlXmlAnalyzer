using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record TuningSessionLoadResult(
        IReadOnlyList<PlanSnapshot> Snapshots,
        PlanSnapshot? PlanA,
        PlanSnapshot? PlanB)
    {
        public PlanComparisonSelection? ComparisonSelection { get; init; }
        public string SourceVersion { get; init; } = "unversioned";
        public bool RequiresMigration => SourceVersion != TuningSessionService.CurrentVersion;
    }

    public sealed partial class TuningSessionService
    {
        public const string CurrentVersion = "2.1";
        public const int MaxSnapshots = 1024;
        private readonly ITuningSessionWriter _writer;
        private readonly IUnexpectedErrorReporter _unexpectedErrors;

        public TuningSessionService() : this(null, null) { }

        public TuningSessionService(ITuningSessionWriter? writer, IUnexpectedErrorReporter? unexpectedErrors = null)
        {
            _writer = writer ?? new TuningSessionWriter();
            _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
        }
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
            double? cost = ParseDouble(rootRelOp?.Attribute("EstimatedTotalSubtreeCost")?.Value);
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

            var identities = PlanIdentityAdapter.GetDocument(currentPlanDocument);
            var snapshotDocument = new XDocument(currentPlanDocument);
            if (identities != null) PlanIdentityAdapter.SetEnvelope(snapshotDocument, identities.Envelope);
            if (identities?.Statements.Count > 1)
                statementText = string.Join(Environment.NewLine, identities.Statements.Select(statement =>
                    $"Batch {statement.Key.Batch.BatchOrdinal} / Statement {statement.Key.StatementOrdinal}: {statement.Text ?? "N/A"}"));

            return new PlanSnapshot
            {
                Title = $"Plan version #{versionNumber} - {fileName}",
                FilePath = currentPlanFilePath ?? string.Empty,
                CaptureTime = captureTime ?? DateTime.Now,
                Document = snapshotDocument,
                TotalCost = CapturedTotal(identities) ?? (identities?.QueryPlans.Count > 0 ? null : cost),
                OriginalSourceHash = identities?.Envelope.SourceHash,
                OriginalSourceXmlHash = PlanSnapshot.ComputeXmlHash(snapshotDocument),
                OperatorCount = operatorCount,
                MissingIndexCount = missingIndexCount,
                StatementText = statementText
            };
        }

        public void Save(
            string filePath,
            IEnumerable<PlanSnapshot> snapshots,
            PlanSnapshot? planA,
            PlanSnapshot? planB,
            PlanComparisonSelection? comparisonSelection = null) =>
            Save(filePath, snapshots, planA, planB, comparisonSelection, CancellationToken.None);

        public void Save(string filePath, IEnumerable<PlanSnapshot> snapshots, PlanSnapshot? planA, PlanSnapshot? planB,
            PlanComparisonSelection? comparisonSelection, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Logger.Debug("IMP28.Session.Save started.");
                SaveCore(filePath, snapshots, planA, planB, comparisonSelection, cancellationToken);
                Logger.Debug("IMP28.Session.Save completed; original files retained.");
            }
            catch (Exception exception)
            {
                ExceptionPolicy.Describe(exception, "IMP28.Session.Save", _unexpectedErrors);
                throw;
            }
        }

        private void SaveCore(string filePath, IEnumerable<PlanSnapshot> snapshots, PlanSnapshot? planA, PlanSnapshot? planB,
            PlanComparisonSelection? comparisonSelection, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new InvalidDataException("SESSION_PATH_INVALID: 请使用新的 .pesession 文件名。");
            }

            var captured = snapshots.Take(MaxSnapshots + 1).ToArray();
            if (captured.Length > MaxSnapshots) throw new InvalidDataException("SESSION_BUDGET_EXCEEDED: 会话最多保存 1024 个快照。");
            var snapshotElements = captured.Select(snapshot =>
                new XElement(
                    SessionNamespace + "Snapshot",
                    new XAttribute("Id", snapshot.Id),
                    new XAttribute("Title", snapshot.Title),
                    new XAttribute("FilePath", snapshot.FilePath),
                    new XAttribute("CaptureTime", snapshot.CaptureTime.ToString("o", CultureInfo.InvariantCulture)),
                    snapshot.TotalCost.HasValue ? new XAttribute("TotalCost", snapshot.TotalCost.Value.ToString(CultureInfo.InvariantCulture)) : null,
                    (snapshot.CapturedOriginalSourceHash ?? PlanSnapshot.NormalizeHash(snapshot.OriginalSourceHash)) is not { } sourceHash
                        ? null : new XAttribute("OriginalSourceHash", sourceHash),
                    new XAttribute("OriginalSourceHashBound", snapshot.CapturedOriginalSourceHash != null),
                    new XAttribute("XmlContentHash", snapshot.XmlContentHash),
                    new XAttribute("XmlContentHashFormat", "xml-representation/1"),
                    comparisonSelection == null ? WriteSelection(SessionNamespace + "ComparisonSelection", snapshot.SelectedQueryPlan, snapshot) : null,
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
                new XAttribute("Version", CurrentVersion),
                new XAttribute("Privacy", Privacy.OutputPrivacy.RawNotice),
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
            if (comparisonSelection != null)
                root.Add(new XElement(SessionNamespace + "ComparisonSelection",
                    WriteSelection(SessionNamespace + "A", comparisonSelection.A, planA),
                    WriteSelection(SessionNamespace + "B", comparisonSelection.B, planB)));

            var document = new XDocument(root);
            ValidateStructure(document);
            SaveNewFile(filePath, document, cancellationToken);
        }

        public TuningSessionLoadResult Load(string filePath)
        {
            try
            {
                Logger.Debug("IMP28.Session.Load started.");
                var result = LoadCore(SafeXmlHelper.LoadSafe(filePath));
                if (result.RequiresMigration) Logger.Warning("IMP28.Session legacy format loaded; save a new copy to upgrade.");
                Logger.Debug($"IMP28.Session.Load completed; snapshots={result.Snapshots.Count}.");
                return result;
            }
            catch (Exception exception)
            {
                ExceptionPolicy.Describe(exception, "IMP28.Session.Load", _unexpectedErrors);
                throw;
            }
        }

        private static TuningSessionLoadResult LoadCore(XDocument document)
        {
            string version = ValidateStructure(document);
            XElement? snapshotsElement = document.Root!.Element(SessionNamespace + "Snapshots");
            if (snapshotsElement == null)
            {
                return new TuningSessionLoadResult(
                    Array.Empty<PlanSnapshot>(),
                    null,
                    null) { SourceVersion = version };
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

            var selections = document.Root.Elements(SessionNamespace + "ComparisonSelection").ToArray();
            if (selections.Length > 1 || selections.Any(e => e.Elements().Any(c => c.Name != SessionNamespace + "A" && c.Name != SessionNamespace + "B")
                || e.Elements(SessionNamespace + "A").Count() > 1 || e.Elements(SessionNamespace + "B").Count() > 1))
                throw new InvalidDataException("PLAN_SELECTION_NOT_FOUND: 会话包含重复或未知的比较侧。");
            return new TuningSessionLoadResult(
                snapshots,
                planA,
                planB)
            {
                SourceVersion = version,
                ComparisonSelection = selections.Length == 0 ? null : new(
                    ReadSelection(selections[0].Element(SessionNamespace + "A"), planA?.Document),
                    ReadSelection(selections[0].Element(SessionNamespace + "B"), planB?.Document))
            };
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

            string actualXmlHash = PlanSnapshot.ComputeXmlHash(planDocument);
            string? savedXmlHash = (string?)snapshotElement.Attribute("XmlContentHash");
            string? hashFormat = (string?)snapshotElement.Attribute("XmlContentHashFormat");
            if (hashFormat != null && hashFormat != "xml-representation/1")
                throw new InvalidDataException("SESSION_HASH_FORMAT_UNSUPPORTED: 不支持的会话 XML 指纹格式。");
            bool hashBound = savedXmlHash != null && PlanSnapshot.NormalizeHash(savedXmlHash) == actualXmlHash;
            if (savedXmlHash != null && !hashBound)
            {
                // IMP-17 v2.0 wrote indented session XML after hashing a compact PlanDoc.
                // Accept that historical representation only when its compact hash actually matches.
                var compact = new XDocument(planDocument);
                foreach (var text in compact.DescendantNodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).ToArray()) text.Remove();
                if (hashFormat != null)
                    throw new InvalidDataException("SESSION_CONTENT_HASH_MISMATCH: 会话计划内容与保存的 XML 指纹不一致。");
                hashBound = PlanSnapshot.NormalizeHash(savedXmlHash) == PlanSnapshot.ComputeXmlHash(compact);
                // Old sessions discarded document-level whitespace/comments when storing Root.
                // Keep their byte hash as an unverified historical record if no representation matches.
                if (!hashBound) Logger.Warning("旧会话 XML 指纹关联无法核验；原始文件指纹仅作为历史记录。");
            }
            string? originalSourceHash = (string?)snapshotElement.Attribute("OriginalSourceHash");
            if (originalSourceHash != null && PlanSnapshot.NormalizeHash(originalSourceHash) == null)
                throw new InvalidDataException("SESSION_SOURCE_HASH_INVALID: 会话原始文件指纹格式无效。");

            return new PlanSnapshot
            {
                Title = title,
                FilePath = originalPath,
                CaptureTime = captureTime == default ? DateTime.Now : captureTime,
                Document = planDocument,
                TotalCost = CapturedTotal(PlanIdentityAdapter.GetDocument(planDocument)),
                OriginalSourceHash = (string?)snapshotElement.Attribute("OriginalSourceHash"),
                OriginalSourceXmlHash = hashBound && (string?)snapshotElement.Attribute("OriginalSourceHashBound") != "false" ? actualXmlHash : null,
                SelectedQueryPlan = ReadSelection(snapshotElement.Element(SessionNamespace + "ComparisonSelection"), planDocument),
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

        private static double? CapturedTotal(Models.PlanDocument? model)
        {
            var statements = model?.Statements.Where(s => s.Parent == null && s.QueryPlans.Count > 0).ToArray();
            if (statements == null || statements.Length == 0 || statements.Any(s => s.QueryPlans.Count != 1)) return null;
            double total = 0;
            foreach (var statement in statements)
            {
                var roots = statement.QueryPlans[0].Operators.Where(o => o.Parent == null).ToArray();
                if (roots.Length != 1 || roots[0].Facts?.SubtreeCost.Value is not { } value || !double.IsFinite(total += value)) return null;
            }
            return total;
        }

        private static XElement? WriteSelection(XName name, Models.PlanQueryPlanKey? selection, PlanSnapshot? snapshot)
        {
            if (selection == null) return null;
            if (snapshot?.IdentityModel?.QueryPlans.Any(q => q.Key == selection) != true)
                throw new InvalidDataException("PLAN_SELECTION_NOT_FOUND: 保存的比较选择不属于当前快照。");
            return new XElement(name, new XAttribute("Batch", selection.Statement.Batch.BatchOrdinal),
                new XAttribute("Statement", selection.Statement.StatementOrdinal), new XAttribute("QueryPlan", selection.QueryPlanOrdinal));
        }

        private static Models.PlanQueryPlanKey? ReadSelection(XElement? element, XDocument? document)
        {
            if (element == null) return null;
            if (!int.TryParse((string?)element.Attribute("Batch"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int batch) || batch <= 0
                || !int.TryParse((string?)element.Attribute("Statement"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int statement) || statement <= 0
                || !int.TryParse((string?)element.Attribute("QueryPlan"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int query) || query <= 0)
                throw new InvalidDataException("PLAN_SELECTION_NOT_FOUND: 会话比较选择无效。");
            return PlanIdentityAdapter.GetDocument(document)?.QueryPlans.SingleOrDefault(q => q.Key.Statement.Batch.BatchOrdinal == batch
                && q.Key.Statement.StatementOrdinal == statement && q.Key.QueryPlanOrdinal == query)?.Key
                ?? throw new InvalidDataException("PLAN_SELECTION_NOT_FOUND: 会话比较选择不存在。");
        }

        private static double? ParseDouble(string? value)
        {
            return double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double parsed)
                && double.IsFinite(parsed) && parsed >= 0 ? parsed : null;
        }
    }
}
