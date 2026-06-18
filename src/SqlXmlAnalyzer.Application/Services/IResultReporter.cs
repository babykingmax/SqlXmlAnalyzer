using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Application.Services
{
    public interface IResultReporter
    {
        void Report(RefactorResult result);
        void Report(RefactorResult result, bool isDryRun, string? outputPath = null);
    }
}
