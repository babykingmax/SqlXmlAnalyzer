using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Core.Abstractions
{
    public interface IAnalysisEngine
    {
        AnalysisReport Analyze(string xmlContent);
    }
}
