using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;

namespace SqlXmlAnalyzer.Core.Services;

/// <summary>Exact captured object and QueryPlan association. No collation or default schema is inferred.</summary>
public static class IndexTargetResolver
{
    public sealed record SortColumn(string Name, bool Ascending);
    public static IReadOnlyList<XElement> FindOperators(MissingIndexSuggestion suggestion, XDocument? document, XNamespace ns)
    {
        if (document == null || suggestion.Location?.QueryPlan == null) return Array.Empty<XElement>();
        var target = IndexDdlCompiler.ResolveTarget(suggestion);
        if (target.Database == null || target.Schema == null || target.Object == null) return Array.Empty<XElement>();
        var model = PlanIdentityAdapter.GetDocument(document);
        return model?.Operators.Where(op => op.Key.QueryPlan == suggestion.Location.QueryPlan
                && op.Objects.Count == 1 && op.Objects[0].Identity == target)
            .Select(op => model.GetOperatorSource(op.Key)!).ToArray() ?? Array.Empty<XElement>();
    }

    public static bool BindSqlSuggestion(MissingIndexSuggestion suggestion, XElement statement, XNamespace ns)
    {
        var model = PlanIdentityAdapter.GetDocument(statement.Document);
        var statementLocation = model?.FindLocation(statement);
        var target = IndexDdlCompiler.ResolveTarget(suggestion);
        bool Matches(SqlObjectIdentity candidate) => candidate.Database != null && candidate.Schema != null && candidate.Object != null
            && candidate.Object == target.Object
            && (target.Server == null || target.Server == candidate.Server)
            && (target.Database == null || target.Database == candidate.Database)
            && (target.Schema == null || target.Schema == candidate.Schema);
        var matches = model?.Operators.Where(op => op.Location.Statement == statementLocation?.Statement)
            .SelectMany(op => op.Objects).Where(reference => Matches(reference.Identity))
            .GroupBy(reference => (reference.Identity, reference.Location.QueryPlan)).Select(group => group.First()).Take(2).ToArray();
        if (matches?.Length != 1)
        {
            suggestion.Location = statementLocation;
            Logger.Warning("IMP-15: INDEX_TARGET_UNRESOLVED；SQL 候选无法唯一关联到捕获计划对象。");
            return false;
        }
        var match = matches[0];
        suggestion.ObjectIdentity = match.Identity;
        suggestion.Server = match.Identity.Server == null ? null : SqlObjectIdentity.Quote(match.Identity.Server);
        suggestion.Database = SqlObjectIdentity.Quote(match.Identity.Database!);
        suggestion.Schema = SqlObjectIdentity.Quote(match.Identity.Schema!);
        suggestion.Table = SqlObjectIdentity.Quote(match.Identity.Object!);
        suggestion.Location = match.Location with { Operator = null };
        Logger.Debug("IMP-15: SQL 索引候选已关联到唯一的语句、QueryPlan 与完整对象。");
        return true;
    }

    public static IEnumerable<XElement> LocalElements(XElement relOp, XNamespace ns) => relOp.Descendants()
        .Where(element => element.Name.Namespace == ns && !element.Ancestors().TakeWhile(a => a != relOp)
            .Any(a => a.Name.Namespace != ns || a.Name.LocalName is "RelOp" or "InternalInfo"));

    public static IEnumerable<XElement> FindColumns(MissingIndexSuggestion suggestion, XDocument? document, XNamespace ns)
    {
        var target = IndexDdlCompiler.ResolveTarget(suggestion);
        var references = FindOperators(suggestion, document, ns).SelectMany(op => LocalElements(op, ns)
            .Where(e => e.Name == ns + "ColumnReference")).ToArray();
        var columns = references.Where(column => TryReadEntityColumn(column, target, out _)).ToArray();
        Logger.Debug($"IMP-15: 实体列身份核验完成；有效 {columns.Length}，未采用 {references.Length - columns.Length}。");
        return columns;
    }

    /// <summary>Each result is one complete Sort order in the captured QueryPlan, never a merged column list.</summary>
    public static IReadOnlyList<IReadOnlyList<SortColumn>> FindSortOrders(MissingIndexSuggestion suggestion, XDocument? document, XNamespace ns)
    {
        var orders = new List<IReadOnlyList<SortColumn>>();
        if (document == null || suggestion.Location?.QueryPlan == null) return orders;
        var target = IndexDdlCompiler.ResolveTarget(suggestion);
        var model = PlanIdentityAdapter.GetDocument(document);
        var query = model?.QueryPlans.FirstOrDefault(q => q.Key == suggestion.Location.QueryPlan);
        if (query == null) return orders;
        foreach (var op in query.Operators)
        {
            if (op.PhysicalOp != "Sort") continue;
            var source = model!.GetOperatorSource(op.Key)!;
            // Sort owns OrderBy, while the scanned Object belongs to its child RelOp.
            foreach (var sort in source.Elements(ns + "Sort"))
            {
                var orderByElements = sort.Elements(ns + "OrderBy").ToArray();
                if (orderByElements.Length != 1) continue;
                var entries = orderByElements[0].Elements().ToArray();
                if (entries.Length == 0) continue;
                var columns = new List<SortColumn>();
                foreach (var entry in entries)
                {
                    var references = entry.Elements().ToArray();
                    string? direction = ((string?)entry.Attribute("Ascending"))?.Trim();
                    if (entry.Name != ns + "OrderByColumn" || references.Length != 1
                        || references[0].Name != ns + "ColumnReference"
                        || !TryReadEntityColumn(references[0], target, out string name)
                        || direction is not ("1" or "0" or "true" or "false"))
                    {
                        columns.Clear();
                        break;
                    }
                    columns.Add(new(name, direction is "1" or "true"));
                }
                if (columns.Count != 0) orders.Add(columns.AsReadOnly());
            }
        }
        Logger.Debug($"IMP-15: 当前 QueryPlan 的完整排序证据 {orders.Count} 项。");
        return orders.AsReadOnly();
    }

    // ColumnReference also represents parameters, bookmarks and computed expressions.
    // Do not infer entity ownership from the surrounding operator or the name prefix.
    internal static bool TryReadEntityColumn(XElement column, SqlObjectIdentity target, out string name)
    {
        name = "";
        if (target.Database == null || target.Schema == null || target.Object == null
            || column.Attribute("ParameterDataType") != null || column.Attribute("ParameterCompiledValue") != null
            || column.Attribute("ParameterRuntimeValue") != null) return false;
        try
        {
            string? Part(string attribute)
            {
                string? value = (string?)column.Attribute(attribute);
                if (value == null) return null;
                string decoded = IndexDdlCompiler.DecodeName(value);
                IndexDdlCompiler.QuoteIdentifier(decoded);
                return decoded;
            }
            if (Part("Server") != target.Server || Part("Database") != target.Database
                || Part("Schema") != target.Schema || Part("Table") != target.Object) return false;
            // Showplan ColumnReference/@Column is a raw column name, unlike the
            // delimited object parts and MissingIndex/Column/@Name DDL tokens.
            name = (string?)column.Attribute("Column") ?? "";
            IndexDdlCompiler.QuoteIdentifier(name);
            return true;
        }
        catch (InvalidDataException)
        {
            // Unproven input is not an entity column; never log raw SQL or identifiers.
            return false;
        }
    }
}
