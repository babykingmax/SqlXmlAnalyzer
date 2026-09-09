using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Scoring;

public static class IndexScoringCalculator
{
    public static void CalculateScore(MissingIndexSuggestion suggestion, XDocument? planDoc, XNamespace? ns)
    {
        if (suggestion == null) return;
        var result = Evaluate(suggestion, planDoc, ns);
        suggestion.Score = result.Score;
        suggestion.ScoreAssessment = result;
    }

    public static IndexScoreResult Evaluate(MissingIndexSuggestion suggestion, XDocument? planDoc, XNamespace? ns,
        IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(suggestion);
            string Name(IndexColumn column)
            {
                if (column == null) throw new InvalidDataException("INDEX_SCORE_COLUMN_INVALID: 列定义无效。");
                string value = IndexDdlCompiler.DecodeName(column.Name);
                IndexDdlCompiler.QuoteIdentifier(value);
                return value;
            }
            var keys = suggestion.KeyColumns?.Select(Name).ToArray() ?? throw new InvalidDataException("INDEX_SCORE_COLUMNS_MISSING");
            var includes = suggestion.IncludeColumns?.Select(Name).ToArray() ?? throw new InvalidDataException("INDEX_SCORE_COLUMNS_MISSING");
            var equality = new HashSet<string>(StringComparer.Ordinal);
            var inequality = new HashSet<string>(StringComparer.Ordinal);
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<IReadOnlyList<IndexTargetResolver.SortColumn>> orders = Array.Empty<IReadOnlyList<IndexTargetResolver.SortColumn>>();
            string source = "DEFINITION_ROLES_ONLY";
            if (planDoc == null)
            {
                foreach (var column in suggestion.KeyColumns)
                {
                    if (column.Usage == "EQUALITY") equality.Add(Name(column));
                    if (column.Usage == "INEQUALITY") inequality.Add(Name(column));
                }
            }
            else if (ns != null)
            {
                var operators = IndexTargetResolver.FindOperators(suggestion, planDoc, ns);
                var predicates = IndexPredicateEvidence.Read(operators, IndexDdlCompiler.ResolveTarget(suggestion), ns);
                equality = predicates.Equality;
                inequality = predicates.Inequality;
                source = operators.Count == 0 ? "MISSING_TARGET_EVIDENCE" : predicates.UsedTextFallback ? "SCALAR_TEXT_SYNTAX"
                    : equality.Count + inequality.Count > 0 ? "CAPTURED_PREDICATES" : "MISSING_PREDICATE_EVIDENCE";
                orders = IndexTargetResolver.FindSortOrders(suggestion, planDoc, ns);
                outputs.UnionWith(IndexTargetResolver.FindColumns(suggestion, planDoc, ns)
                    .Where(column => column.Parent?.Name == ns + "OutputList" && column.Parent.Parent?.Name == ns + "RelOp")
                    .Select(column => (string)column.Attribute("Column")!));
            }
            else source = "MISSING_NAMESPACE";

            int prefix = keys.TakeWhile(equality.Contains).Count();
            int seq = prefix * 30;
            int sineq = prefix < keys.Length && inequality.Contains(keys[prefix]) ? 15 : 0;
            bool compatible = orders.Any(order => order.Count > 0 && keys.Length - prefix >= order.Count
                && order.All(column => column.Ascending == order[0].Ascending)
                && keys.Skip(prefix).Take(order.Count).SequenceEqual(order.Select(c => c.Name), StringComparer.Ordinal));
            int sorder = compatible ? 15 : 0;
            var indexColumns = keys.Concat(includes).ToHashSet(StringComparer.Ordinal);
            int covered = outputs.Count(indexColumns.Contains);
            int scover = outputs.Count == 0 || keys.Length == 0 ? 0 : (int)Math.Round(40.0 * covered / outputs.Count);
            int penalty = Math.Max(0, keys.Length - 4) * 2 + Math.Max(0, includes.Length - 8) * 2;
            int score = Math.Clamp(seq + sineq + sorder + scover - penalty, 0, 100);
            Logger.Debug($"IMP-16: 评分完成；等值 {seq}，范围 {sineq}，排序 {sorder}，覆盖 {scover}，惩罚 {penalty}；来源 {source}。");
            return new(score, seq, sineq, sorder, scover, penalty, outputs.Count, covered, source, suggestion.Location);
        }
        catch (Exception exception)
        {
            ExceptionPolicy.Describe(exception, "IndexScoringCalculator.Evaluate", unexpectedErrors);
            throw;
        }
    }
}
