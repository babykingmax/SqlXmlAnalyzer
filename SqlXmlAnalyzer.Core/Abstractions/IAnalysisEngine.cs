using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Core.Abstractions
{
    public interface IAnalysisEngine
    {
        AnalysisReport Analyze(string xmlContent);

        AnalysisReport AnalyzeInput(Services.InputRecognitionResult input, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var report = AnalyzeInput(input);
            cancellationToken.ThrowIfCancellationRequested();
            return report;
        }

        // Keep existing adapters compatible while allowing the production engine to
        // analyze an already decoded document without another text round trip.
        AnalysisReport AnalyzeDocument(System.Xml.Linq.XDocument document) => Analyze(document.ToString());

        AnalysisReport AnalyzeInput(Services.InputRecognitionResult input)
        {
            input = input.ForExecutionPlan();
            var result = input.IsSuccess ? AnalyzeDocument(input.Document!) :
                new AnalysisReport(System.Array.Empty<IAnalysisIssue>())
                { InputStatus = input.Status, InputErrorCode = input.ErrorCode, InputErrorMessage = input.ErrorMessage };
            return result with { Plan = result.Plan ?? (input.IsSuccess ? Services.PlanIdentityAdapter.GetDocument(input.Document) : null),
                InputEnvelope = input.Envelope, Capabilities = input.Capabilities, InputDiagnostics = input.Diagnostics };
        }
    }
}
