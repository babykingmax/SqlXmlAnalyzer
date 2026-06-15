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
                    return suggestions; // Return empty if syntax errors exist
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
                    ExtractTableReferences(tableRef, scope.Tables);
                }
            }

            // Initialize dictionaries for tables
            foreach (var table in scope.Tables)
            {
                scope.ColumnUsages[table] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                scope.IncludeColumnCandidates[table] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                        keyCols.Add(new IndexColumn { Name = "[" + kv.Key + "]", Usage = "EQUALITY" });
                    }
                    
                    // Add Inequality keys next
                    foreach (var kv in usages.Where(u => u.Value == "INEQUALITY"))
                    {
                        keyCols.Add(new IndexColumn { Name = "[" + kv.Key + "]", Usage = "INEQUALITY" });
                    }

                    if (keyCols.Count > 0)
                    {
                        var includeCols = new List<IndexColumn>();
                        foreach (var inc in includes)
                        {
                            // A column cannot be both a key column and an include column
                            if (!usages.ContainsKey(inc))
                            {
                                includeCols.Add(new IndexColumn { Name = "[" + inc + "]", Usage = "INCLUDE" });
                            }
                        }

                        _suggestions.Add(new MissingIndexSuggestion
                        {
                            Schema = table.Schema,
                            Table = table.Table,
                            KeyColumns = keyCols,
                            IncludeColumns = includeCols,
                            Impact = 80.0, // Default impact heuristic
                            Score = 0 // Computed later
                        });
                    }
                }
            }
        }

        private void ExtractTableReferences(TableReference tableRef, List<TableReferenceInfo> list)
        {
            if (tableRef is NamedTableReference namedRef)
            {
                string schema = namedRef.SchemaObject.SchemaIdentifier?.Value ?? "dbo";
                string table = namedRef.SchemaObject.BaseIdentifier?.Value ?? "";
                string alias = namedRef.Alias?.Value ?? table;

                list.Add(new TableReferenceInfo { Schema = schema, Table = table, Alias = alias });
            }
            else if (tableRef is QualifiedJoin qualifiedJoin)
            {
                ExtractTableReferences(qualifiedJoin.FirstTableReference, list);
                ExtractTableReferences(qualifiedJoin.SecondTableReference, list);
            }
            else if (tableRef is UnqualifiedJoin unqualifiedJoin)
            {
                ExtractTableReferences(unqualifiedJoin.FirstTableReference, list);
                ExtractTableReferences(unqualifiedJoin.SecondTableReference, list);
            }
            else if (tableRef is JoinParenthesisTableReference parenthesizedRef)
            {
                ExtractTableReferences(parenthesizedRef.Join, list);
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

                foreach (var scope in _scopes)
                {
                    foreach (var table in scope.Tables)
                    {
                        if (string.Equals(table.Alias, qualifier, StringComparison.OrdinalIgnoreCase))
                        {
                            return table;
                        }
                        if (string.Equals(table.Table, qualifier, StringComparison.OrdinalIgnoreCase))
                        {
                            if (schemaQualifier == null || string.Equals(table.Schema, schemaQualifier, StringComparison.OrdinalIgnoreCase))
                            {
                                return table;
                            }
                        }
                    }
                }
            }

            foreach (var scope in _scopes)
            {
                if (scope.Tables.Count > 0)
                {
                    return scope.Tables[0];
                }
            }

            return tables.Count > 0 ? tables[0] : null;
        }

        private string GetColumnName(ColumnReferenceExpression colRef)
        {
            var mpi = colRef.MultiPartIdentifier;
            if (mpi == null || mpi.Identifiers.Count == 0) return "";
            return mpi.Identifiers[mpi.Identifiers.Count - 1].Value;
        }

        private class TableReferenceInfo
        {
            public string Schema { get; set; } = "dbo";
            public string Table { get; set; } = "";
            public string Alias { get; set; } = "";
        }

        private class QueryScope
        {
            public List<TableReferenceInfo> Tables { get; } = new();
            public Dictionary<TableReferenceInfo, Dictionary<string, string>> ColumnUsages { get; } = new();
            public Dictionary<TableReferenceInfo, HashSet<string>> IncludeColumnCandidates { get; } = new();
        }

        private class ColumnFinder : TSqlFragmentVisitor
        {
            public List<ColumnReferenceExpression> Columns { get; } = new();
            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                Columns.Add(node);
                base.ExplicitVisit(node);
            }
        }

        private class PredicateVisitor : TSqlFragmentVisitor
        {
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
