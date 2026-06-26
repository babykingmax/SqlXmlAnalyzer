using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockDocumentResult(
        XDocument Document,
        string FilePath,
        DeadlockAnalysisOutput Analysis);

    public sealed class DeadlockDocumentController
    {
        private readonly DeadlockAnalysisService _analysisService;

        public DeadlockDocumentController(DeadlockAnalysisService analysisService)
        {
            _analysisService = analysisService;
        }

        public Task<DeadlockDocumentResult> AnalyzeAsync(
            XDocument document,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DeadlockAnalysisOutput analysis = _analysisService.Analyze(
                        document,
                        cancellationToken);
                    return new DeadlockDocumentResult(
                        document,
                        filePath,
                        analysis);
                },
                cancellationToken);
        }
    }
}
