using System.Collections.Generic;
using SqlXmlAnalyzer.Core.Abstractions;

namespace SqlXmlAnalyzer.Core
{
    public record AnalysisReport(IReadOnlyList<IAnalysisIssue> Issues);
}
