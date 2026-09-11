using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanDocumentResult(
        XDocument Document,
        string FilePath,
        XNamespace ShowplanNamespace,
        PlanAnalysisOutput Analysis)
    {
        public InputRecognitionResult? Input { get; init; }
    }

    public sealed class PlanDocumentController
    {
        private readonly PlanAnalysisService _analysisService;

        public PlanDocumentController(PlanAnalysisService analysisService)
        {
            _analysisService = analysisService;
        }

        public Task<PlanDocumentResult> AnalyzeAsync(
            XDocument document,
            string filePath,
            XNamespace showplanNamespace,
            CancellationToken cancellationToken = default,
            InputRecognitionResult? input = null)
        {
            var source = new PlanAnalysisSource(document, filePath, input);
            return Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PlanAnalysisOutput analysis = _analysisService.Analyze(
                        document,
                        showplanNamespace,
                        filePath,
                        cancellationToken, source.Input);
                    return new PlanDocumentResult(
                        document,
                        filePath,
                        showplanNamespace,
                        analysis) { Input = source.Input };
                },
                cancellationToken);
        }
    }
}
