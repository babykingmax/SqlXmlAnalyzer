using System.Collections.Generic;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Abstractions
{
    public interface IRuleFilter
    {
        IEnumerable<ISqlRefactorRule> Filter(IEnumerable<ISqlRefactorRule> rules, RefactorOptions options);
    }
}
