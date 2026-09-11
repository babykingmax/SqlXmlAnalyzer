using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        public InputStatus Status { get; init; } = InputStatus.Success;
        public string? ErrorCode { get; init; }
        public System.Collections.Generic.IReadOnlyList<DeadlockInput> Deadlocks { get; init; } = Array.Empty<DeadlockInput>();
        public bool IsSuccess => Status == InputStatus.Success && Kind != AnalysisDocumentKind.Unknown && ErrorMessage == null;
        public bool HasUsableContent => Input?.HasUsableContent == true;
        public InputRecognitionResult? Input { get; init; }
    }

    public enum DocumentDropActionStatus
    {
        Ready,
        Empty,
        Unsupported
    }

    public sealed record DocumentDropActionResult(
        DocumentDropActionStatus Status,
        AnalysisDocumentKind Kind,
        string FilePath,
        string? UserMessage);

    public sealed class DocumentOpenService
    {
        private readonly IDiagnosticDocumentReader _recognition;

        public DocumentOpenService(IDiagnosticDocumentReader? recognition = null)
        {
            _recognition = recognition ?? new InputRecognitionService();
        }

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

        public DocumentDropActionResult BuildDropAction(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new DocumentDropActionResult(
                    DocumentDropActionStatus.Empty,
                    AnalysisDocumentKind.Unknown,
                    string.Empty,
                    null);
            }

            AnalysisDocumentKind kind = ClassifyDroppedPath(filePath);
            // Extensions are routing hints only; every XML candidate is checked by OpenAsync.
            return new DocumentDropActionResult(
                DocumentDropActionStatus.Ready,
                kind,
                filePath,
                null);
        }

        public async Task<DocumentOpenResult> OpenAsync(
            string filePath,
            CancellationToken cancellationToken = default,
            Action<AnalysisOperationState>? progress = null)
        {
            InputRecognitionResult result;
            try
            {
                result = _recognition is InputRecognitionService recognition
                    ? await Task.Run(() => recognition.Load(filePath, cancellationToken, progress), cancellationToken).ConfigureAwait(false)
                    : await _recognition.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result = new(InputStatus.Cancelled, AnalysisDocumentKind.Unknown, null, "INPUT_CANCELLED", "读取已取消。");
            }

            return new DocumentOpenResult(
                result.Kind,
                filePath,
                result.Document,
                result.ErrorMessage)
            { Status = result.Status, ErrorCode = result.ErrorCode, Deadlocks = result.Deadlocks, Input = result };
        }

        public AnalysisDocumentKind ClassifyXml(XDocument document)
        {
            return InputRecognitionService.Recognize(document).Kind;
        }
    }
}
