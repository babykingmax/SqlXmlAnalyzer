using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanDocumentResult(
        XDocument Document,
        string FilePath,
        XNamespace ShowplanNamespace,
        PlanAnalysisOutput Analysis);

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
            CancellationToken cancellationToken = default)
        {
            return Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PlanAnalysisOutput analysis = _analysisService.Analyze(
                        document,
                        showplanNamespace,
                        filePath,
                        cancellationToken);
                    return new PlanDocumentResult(
                        document,
                        filePath,
                        showplanNamespace,
                        analysis);
                },
                cancellationToken);
        }
    }
}
