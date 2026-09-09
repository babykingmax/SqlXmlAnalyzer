using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public class MissingIndexSuggester
    {
        public static List<MissingIndexSuggestion> SuggestIndexes(string sql)
        {
            var suggestions = new List<MissingIndexSuggestion>();
            if (string.IsNullOrWhiteSpace(sql)) return suggestions;

            var parser = new TSql160Parser(true);
            using (var reader = new StringReader(sql))
            {
                var fragment = parser.Parse(reader, out var errors);
                if (errors.Count > 0)
                {
                    Logger.Warning("IMP-15: INDEX_SQL_INVALID；SQL 无法解析，未生成索引候选。");
                    return suggestions;
                }

                suggestions.AddRange(SuggestIndexes(fragment));
            }
            return suggestions;
        }

        public static List<MissingIndexSuggestion> SuggestIndexes(TSqlFragment fragment)
        {
            var visitor = new IndexSuggestionVisitor();
            fragment.Accept(visitor);
            return visitor.GetSuggestions();
        }
    }

    public class IndexSuggestionVisitor : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _cteNames = new(StringComparer.Ordinal);
        public override void ExplicitVisit(SelectStatement node)
        {
            var previous = _cteNames.ToArray();
            foreach (var cte in node.WithCtesAndXmlNamespaces?.CommonTableExpressions ?? Enumerable.Empty<CommonTableExpression>())
                _cteNames.Add(cte.ExpressionName.Value);
            try { base.ExplicitVisit(node); }
            finally { _cteNames.Clear(); _cteNames.UnionWith(previous); }
        }
        private readonly List<MissingIndexSuggestion> _suggestions = new();
        private readonly Stack<QueryScope> _scopes = new();

        public List<MissingIndexSuggestion> GetSuggestions() => _suggestions;

        public override void ExplicitVisit(QuerySpecification node)
        {
            var scope = new QueryScope();
            _scopes.Push(scope);

            // 1. Extract tables from FROM clause
            if (node.FromClause != null)
            {
                foreach (var tableRef in node.FromClause.TableReferences)
                {
                    ExtractTableReferences(tableRef, scope);
                }
            }

            // Initialize dictionaries for tables
            foreach (var table in scope.Tables)
            {
                scope.ColumnUsages[table] = new Dictionary<string, string>(StringComparer.Ordinal);
                scope.IncludeColumnCandidates[table] = new HashSet<string>(StringComparer.Ordinal);
            }

            // 2. Extract key columns from WHERE clause
            if (node.WhereClause != null)
            {
                var predVisitor = new PredicateVisitor(scope, this);
                node.WhereClause.Accept(predVisitor);
            }

            // 3. Extract key columns from JOIN ON clauses
            if (node.FromClause != null)
            {
                var joinVisitor = new PredicateVisitor(scope, this);
                node.FromClause.Accept(joinVisitor);
            }

            // 4. Extract other referenced columns from SELECT list, GROUP BY
            var colFinder = new ColumnFinder();
            foreach (var element in node.SelectElements)
            {
                element.Accept(colFinder);
            }
            if (node.GroupByClause != null)
            {
                node.GroupByClause.Accept(colFinder);
            }

            foreach (var colRef in colFinder.Columns)
            {
                var table = ResolveTable(colRef, scope.Tables);
                if (table != null)
                {
                    string colName = GetColumnName(colRef);
                    if (!string.IsNullOrEmpty(colName))
                    {
                        var targetScope = _scopes.FirstOrDefault(s => s.Tables.Contains(table));
                        if (targetScope != null)
                        {
                            targetScope.IncludeColumnCandidates[table].Add(colName);
                        }
                    }
                }
            }

            base.ExplicitVisit(node);

            if (_scopes.Count > 0)
            {
                var scopeOut = _scopes.Pop();

                foreach (var table in scopeOut.Tables)
                {
                    var usages = scopeOut.ColumnUsages[table];
                    var includes = scopeOut.IncludeColumnCandidates[table];

                    var keyCols = new List<IndexColumn>();

                    // Add Equality keys first
                    foreach (var kv in usages.Where(u => u.Value == "EQUALITY"))
                    {
                        keyCols.Add(new IndexColumn { Name = IndexDdlCompiler.QuoteIdentifier(kv.Key), Usage = "EQUALITY" });
                    }

                    // Add Inequality keys next
                    foreach (var kv in usages.Where(u => u.Value == "INEQUALITY"))
                    {
                        keyCols.Add(new IndexColumn { Name = IndexDdlCompiler.QuoteIdentifier(kv.Key), Usage = "INEQUALITY" });
                    }

                    if (keyCols.Count > 0)
                    {
                        var includeCols = new List<IndexColumn>();
                        foreach (var inc in includes)
                        {
                            // A column cannot be both a key column and an include column
                            if (!usages.ContainsKey(inc))
                            {
                                includeCols.Add(new IndexColumn { Name = IndexDdlCompiler.QuoteIdentifier(inc), Usage = "INCLUDE" });
                            }
                        }

                        _suggestions.Add(new MissingIndexSuggestion
                        {
                            Schema = table.Schema,
                            Table = table.Table,
                            Database = table.Database,
                            Server = table.Server,
                            ObjectIdentity = new(table.Server, table.Database, string.IsNullOrEmpty(table.Schema) ? null : table.Schema, table.Table),
                            KeyColumns = keyCols,
                            IncludeColumns = includeCols,
                            Impact = 0, // No SQL Server Impact was captured for a syntax-derived candidate.
                            Source = IndexSuggestionSource.SqlSyntax,
                            Score = 0 // Computed later
                        });
                    }
                }
            }
        }

        private void ExtractTableReferences(TableReference tableRef, QueryScope scope)
        {
            var list = scope.Tables;
            if (tableRef is NamedTableReference namedRef)
            {
                if (namedRef.SchemaObject.Identifiers.Count == 1 && _cteNames.Contains(namedRef.SchemaObject.BaseIdentifier.Value))
                {
                    scope.HasUnresolvedSources = true;
                    scope.UnresolvedQualifiers.Add(namedRef.Alias?.Value ?? namedRef.SchemaObject.BaseIdentifier.Value);
                    return;
                }
                string schema = namedRef.SchemaObject.SchemaIdentifier?.Value ?? "";
                string table = namedRef.SchemaObject.BaseIdentifier?.Value ?? "";
                string alias = namedRef.Alias?.Value ?? table;

                list.Add(new TableReferenceInfo { Schema = schema, Table = table, Alias = alias,
                    HasAlias = namedRef.Alias != null, Database = namedRef.SchemaObject.DatabaseIdentifier?.Value,
                    Server = namedRef.SchemaObject.ServerIdentifier?.Value });
            }
            else if (tableRef is QualifiedJoin qualifiedJoin)
            {
                ExtractTableReferences(qualifiedJoin.FirstTableReference, scope);
                ExtractTableReferences(qualifiedJoin.SecondTableReference, scope);
            }
            else if (tableRef is UnqualifiedJoin unqualifiedJoin)
            {
                ExtractTableReferences(unqualifiedJoin.FirstTableReference, scope);
                ExtractTableReferences(unqualifiedJoin.SecondTableReference, scope);
            }
            else if (tableRef is JoinParenthesisTableReference parenthesizedRef)
            {
                ExtractTableReferences(parenthesizedRef.Join, scope);
            }
            else
            {
                scope.HasUnresolvedSources = true;
                if (tableRef is TableReferenceWithAlias withAlias && withAlias.Alias != null)
                    scope.UnresolvedQualifiers.Add(withAlias.Alias.Value);
            }
        }

        private TableReferenceInfo? ResolveTable(ColumnReferenceExpression colRef, List<TableReferenceInfo> tables)
        {
            var mpi = colRef.MultiPartIdentifier;
            if (mpi == null || mpi.Identifiers.Count == 0) return null;

            if (mpi.Identifiers.Count > 1)
            {
                string qualifier = mpi.Identifiers[mpi.Identifiers.Count - 2].Value;
                string? schemaQualifier = mpi.Identifiers.Count > 2 ? mpi.Identifiers[mpi.Identifiers.Count - 3].Value : null;

                string? database = mpi.Identifiers.Count > 3 ? mpi.Identifiers[^4].Value : null;
                string? server = mpi.Identifiers.Count > 4 ? mpi.Identifiers[^5].Value : null;
                foreach (var scope in _scopes)
                {
                    if (scope.UnresolvedQualifiers.Contains(qualifier)) return null;
                    var matches = scope.Tables.Where(table => mpi.Identifiers.Count == 2 && table.Alias == qualifier
                        || !table.HasAlias && table.Table == qualifier
                            && (schemaQualifier == null || table.Schema == schemaQualifier)
                            && (database == null || table.Database == database)
                            && (server == null || table.Server == server)).Take(2).ToArray();
                    if (matches.Length != 0) return matches.Length == 1 ? matches[0] : null;
                }
                return null;
            }

            foreach (var scope in _scopes)
            {
                if (scope.HasUnresolvedSources) return null;
                if (scope.Tables.Count > 0)
                {
                    return scope.Tables.Count == 1 ? scope.Tables[0] : null;
                }
            }

            return null;
        }

        private string GetColumnName(ColumnReferenceExpression colRef)
        {
            var mpi = colRef.MultiPartIdentifier;
            if (mpi == null || mpi.Identifiers.Count == 0) return "";
            return mpi.Identifiers[mpi.Identifiers.Count - 1].Value;
        }

        private class TableReferenceInfo
        {
            public string Schema { get; set; } = "";
            public string? Database { get; set; }
            public string? Server { get; set; }
            public bool HasAlias { get; set; }
            public string Table { get; set; } = "";
            public string Alias { get; set; } = "";
        }

        private class QueryScope
        {
            public bool HasUnresolvedSources { get; set; }
            public HashSet<string> UnresolvedQualifiers { get; } = new(StringComparer.Ordinal);
            public List<TableReferenceInfo> Tables { get; } = new();
            public Dictionary<TableReferenceInfo, Dictionary<string, string>> ColumnUsages { get; } = new();
            public Dictionary<TableReferenceInfo, HashSet<string>> IncludeColumnCandidates { get; } = new();
        }

        private class ColumnFinder : TSqlFragmentVisitor
        {
            public override void ExplicitVisit(QuerySpecification node) { }
            public List<ColumnReferenceExpression> Columns { get; } = new();
            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                Columns.Add(node);
                base.ExplicitVisit(node);
            }
        }

        private class PredicateVisitor : TSqlFragmentVisitor
        {
            // Nested query blocks are visited by IndexSuggestionVisitor in their own scope.
            public override void ExplicitVisit(QuerySpecification node) { }
            private readonly QueryScope _scope;
            private readonly IndexSuggestionVisitor _parent;

            public PredicateVisitor(QueryScope scope, IndexSuggestionVisitor parent)
            {
                _scope = scope;
                _parent = parent;
            }

            private void AddPredicate(ColumnReferenceExpression colRef, string usage)
            {
                var table = _parent.ResolveTable(colRef, _scope.Tables);
                if (table != null)
                {
                    string colName = _parent.GetColumnName(colRef);
                    if (!string.IsNullOrEmpty(colName))
                    {
                        var targetScope = _parent._scopes.FirstOrDefault(s => s.Tables.Contains(table));
                        if (targetScope != null)
                        {
                            var usages = targetScope.ColumnUsages[table];
                            if (usages.TryGetValue(colName, out var existingUsage))
                            {
                                // EQUALITY is more restrictive and should override INEQUALITY
                                if (existingUsage == "INEQUALITY" && usage == "EQUALITY")
                                {
                                    usages[colName] = "EQUALITY";
                                }
                            }
                            else
                            {
                                usages[colName] = usage;
                            }
                        }
                    }
                }
            }

            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                if (node.FirstExpression is ColumnReferenceExpression leftCol && node.SecondExpression is ColumnReferenceExpression rightCol)
                {
                    string usage = node.ComparisonType == BooleanComparisonType.Equals ? "EQUALITY" : "INEQUALITY";
                    AddPredicate(leftCol, usage);
                    AddPredicate(rightCol, usage);
                }
                else if (node.FirstExpression is ColumnReferenceExpression firstCol && IsConstantOrVariable(node.SecondExpression))
                {
                    string usage = node.ComparisonType == BooleanComparisonType.Equals ? "EQUALITY" : "INEQUALITY";
                    AddPredicate(firstCol, usage);
                }
                else if (node.SecondExpression is ColumnReferenceExpression secondCol && IsConstantOrVariable(node.FirstExpression))
                {
                    string usage = node.ComparisonType == BooleanComparisonType.Equals ? "EQUALITY" : "INEQUALITY";
                    AddPredicate(secondCol, usage);
                }
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(LikePredicate node)
            {
                if (node.FirstExpression is ColumnReferenceExpression col && IsConstantOrVariable(node.SecondExpression))
                {
                    AddPredicate(col, "INEQUALITY");
                }
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(BooleanTernaryExpression node)
            {
                if ((node.TernaryExpressionType == BooleanTernaryExpressionType.Between ||
                     node.TernaryExpressionType == BooleanTernaryExpressionType.NotBetween) &&
                    node.FirstExpression is ColumnReferenceExpression col &&
                    IsConstantOrVariable(node.SecondExpression) &&
                    IsConstantOrVariable(node.ThirdExpression))
                {
                    AddPredicate(col, "INEQUALITY");
                }
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(InPredicate node)
            {
                if (node.Expression is ColumnReferenceExpression col)
                {
                    AddPredicate(col, "INEQUALITY");
                }
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(BooleanIsNullExpression node)
            {
                if (node.Expression is ColumnReferenceExpression col)
                {
                    string usage = node.IsNot ? "INEQUALITY" : "EQUALITY";
                    AddPredicate(col, usage);
                }
                base.ExplicitVisit(node);
            }

            private bool IsConstantOrVariable(ScalarExpression expr)
            {
                var colFinder = new ColumnFinder();
                expr.Accept(colFinder);
                return colFinder.Columns.Count == 0;
            }
        }
    }
}
