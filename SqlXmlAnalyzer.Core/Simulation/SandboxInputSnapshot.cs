using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Simulation;

public sealed record SandboxInputValue(double Value, string Source, bool IsDefault);
public sealed record SandboxInputSnapshot(SandboxInputValue TotalRows, SandboxInputValue AverageRowSize, SandboxInputValue ReturnedRows)
{
    public const string ModelVersion = "sandbox-input-assumptions/2.0.0";

    public static SandboxInputSnapshot Capture(MissingIndexSuggestion candidate, XDocument? document, XNamespace ns)
    {
        var operators = IndexTargetResolver.FindOperators(candidate, document, ns);
        SandboxInputValue Read(string attribute, double fallback)
        {
            var values = operators.Select(op => (string?)op.Attribute(attribute))
                .Select(text => NumericParser.TryParseInvariantDouble(text, out var value) && double.IsFinite(value) && value >= 0 ? (double?)value : null)
                .OfType<double>().ToArray();
            return values.Length == 0 ? new(fallback, $"工具默认 {fallback}（无有效计划值）", true)
                : new(values.Max(), $"当前目标/QueryPlan 的 {values.Length} 项计划估算最大值（假设）", false);
        }
        return new(Read("TableCardinality", 100000), Read("AvgRowSize", 200), Read("EstimateRows", 1000));
    }
}
