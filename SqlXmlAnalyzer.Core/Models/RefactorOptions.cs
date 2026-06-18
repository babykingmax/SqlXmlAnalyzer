using System.Collections.Generic;

namespace SqlXmlAnalyzer.Core.Models
{
    public record RefactorOptions(
        IReadOnlyList<string>? EnabledRuleIds = null,
        IReadOnlyList<string>? DisabledRuleIds = null,
        int MaxPasses = 5
    );
}
