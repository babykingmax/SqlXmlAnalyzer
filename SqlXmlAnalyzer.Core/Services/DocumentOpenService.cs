using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum AnalysisDocumentKind
    {
        Unknown,
        DeadlockXml,
        ExecutionPlanXml,
        XelDeadlockTrace
    }

    public sealed record DocumentOpenResult(
        AnalysisDocumentKind Kind,
        string FilePath,
        XDocument? Document,
        string? ErrorMessage = null)
    {
        public bool IsSuccess => ErrorMessage == null;
    }

    public sealed class DocumentOpenService
    {
        private const string ShowPlanNamespace =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        public AnalysisDocumentKind ClassifyPath(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return extension.Equals(".xel", StringComparison.OrdinalIgnoreCase)
                ? AnalysisDocumentKind.XelDeadlockTrace
                : AnalysisDocumentKind.Unknown;
        }

        public AnalysisDocumentKind ClassifyDeadlockOpenPath(string filePath)
        {
            return ClassifyPath(filePath) == AnalysisDocumentKind.XelDeadlockTrace
                ? AnalysisDocumentKind.XelDeadlockTrace
                : AnalysisDocumentKind.DeadlockXml;
        }

        public AnalysisDocumentKind ClassifyDroppedPath(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".xdl", StringComparison.OrdinalIgnoreCase))
            {
                return AnalysisDocumentKind.DeadlockXml;
            }

            if (extension.Equals(".xel", StringComparison.OrdinalIgnoreCase))
            {
                return AnalysisDocumentKind.XelDeadlockTrace;
            }

            if (extension.Equals(".sqlplan", StringComparison.OrdinalIgnoreCase))
            {
                return AnalysisDocumentKind.ExecutionPlanXml;
            }

            return AnalysisDocumentKind.Unknown;
        }

        public async Task<DocumentOpenResult> OpenAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new DocumentOpenResult(
                    AnalysisDocumentKind.Unknown,
                    filePath,
                    null,
                    "The specified file does not exist or the path is invalid.");
            }

            AnalysisDocumentKind pathKind = ClassifyPath(filePath);
            if (pathKind == AnalysisDocumentKind.XelDeadlockTrace)
            {
                return new DocumentOpenResult(pathKind, filePath, null);
            }

            XDocument document = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    XDocument loaded = LoadXmlDocument(filePath);
                    cancellationToken.ThrowIfCancellationRequested();
                    return loaded;
                },
                cancellationToken);

            return new DocumentOpenResult(
                ClassifyXml(document),
                filePath,
                document);
        }

        public AnalysisDocumentKind ClassifyXml(XDocument document)
        {
            if (document.Root == null)
            {
                return AnalysisDocumentKind.Unknown;
            }

            if (document.Root.Name.LocalName.Equals("deadlock", StringComparison.OrdinalIgnoreCase))
            {
                return AnalysisDocumentKind.DeadlockXml;
            }

            XName rootName = document.Root.Name;
            if (!rootName.LocalName.Equals("ShowPlanXML", StringComparison.OrdinalIgnoreCase))
            {
                return AnalysisDocumentKind.Unknown;
            }

            string ns = rootName.Namespace.NamespaceName;
            if (ns == ShowPlanNamespace ||
                ns.Contains("showplan", StringComparison.OrdinalIgnoreCase))
            {
                return AnalysisDocumentKind.ExecutionPlanXml;
            }

            return AnalysisDocumentKind.Unknown;
        }

        private static XDocument LoadXmlDocument(string filePath)
        {
            Logger.Info($"Loading XML file: {filePath}");
            try
            {
                XDocument document = SafeXmlHelper.LoadSafe(filePath);
                Logger.Info("Loaded XML file with SafeXmlHelper.LoadSafe.");
                return document;
            }
            catch (XmlException ex) when (
                ex.Message.Contains("encoding", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("BOM", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("character", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("字符", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning(
                    $"XML load hit an encoding/BOM issue: {ex.Message}. Retrying with BOM detection.");
                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                using var reader = new StreamReader(
                    stream,
                    detectEncodingFromByteOrderMarks: true);
                XDocument document = SafeXmlHelper.LoadSafe(reader);
                Logger.Info("Loaded XML file with StreamReader BOM detection.");
                return document;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load XML file: {ex.Message}", ex);
                throw;
            }
        }
    }
}
