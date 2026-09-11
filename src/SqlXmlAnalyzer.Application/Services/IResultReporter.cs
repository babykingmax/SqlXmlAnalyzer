using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Application.Services
{
    public interface IResultReporter
    {
        void Report(RefactorResult result);
        void Report(RefactorResult result, bool isDryRun, string? outputPath = null);
        void Report(RefactorResult result, bool isDryRun, string? outputPath,
            IReadOnlyList<string?> inputPaths, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SqlReportPathGuard.ValidateInputs(inputPaths, outputPath);
            Report(result, isDryRun, outputPath);
        }
    }
}
