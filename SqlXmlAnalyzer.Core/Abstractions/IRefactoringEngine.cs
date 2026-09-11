using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Abstractions
{
    public interface IRefactoringEngine
    {
        RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun);
        RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Run(sql, report, options, isDryRun);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }
}
