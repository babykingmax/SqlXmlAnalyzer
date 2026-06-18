using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Abstractions
{
    public interface IRefactoringEngine
    {
        RefactorResult Run(string sql, AnalysisReport report, RefactorOptions options, bool isDryRun);
    }
}
