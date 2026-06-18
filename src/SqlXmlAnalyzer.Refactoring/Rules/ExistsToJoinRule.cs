using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;

namespace SqlXmlAnalyzer.Refactoring.Rules
{
    public class ExistsToJoinRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_107_EXISTS_JOIN";
        public string Name => "EXISTS to JOIN Optimizer";
        public string Description => "Rewrites simple correlated EXISTS or NOT EXISTS subqueries to INNER JOIN or LEFT JOIN + IS NULL.";
        public int Priority => 55;

        public bool CanApply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new FinderVisitor();
            fragment.Accept(visitor);
            return visitor.Found;
        }

        public RuleResult Apply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new RewriteVisitor(context);
            fragment.Accept(visitor);

            if (visitor.Changed)
            {
                var desc = visitor.ChangeDetail ?? "Converted EXISTS subquery to JOIN";
                return new RuleResult(fragment, true, desc);
            }

            return new RuleResult(fragment, false, null);
        }

        private class FinderVisitor : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(QuerySpecification node)
            {
                if (Found) return;

                if (node.FromClause != null && node.FromClause.TableReferences.Count > 0 &&
                    node.WhereClause != null)
                {
                    var conds = new List<BooleanExpression>();
                    CollectAndConditions(node.WhereClause.SearchCondition, conds);

                    foreach (var cond in conds)
                    {
                        ExistsPredicate? existsPredicate = null;
                        if (cond is ExistsPredicate ep)
                        {
                            existsPredicate = ep;
                        }
                        else if (cond is BooleanNotExpression notExpr && notExpr.Expression is ExistsPredicate nep)
                        {
                            existsPredicate = nep;
                        }

                        if (existsPredicate != null)
                        {
                            bool isNotExists = cond is BooleanNotExpression;
                            if (IsRewriteableExistsSubquery(node, existsPredicate, isNotExists, out _, out _))
                            {
                                Found = true;
                                return;
                            }
                        }
                    }
                }

                base.ExplicitVisit(node);
            }
        }

        private class RewriteVisitor : TSqlFragmentVisitor
        {
            private readonly RefactorContext _context;
            public bool Changed { get; private set; }
            public string? ChangeDetail { get; private set; }

            public RewriteVisitor(RefactorContext context)
            {
                _context = context;
            }

            public override void ExplicitVisit(QuerySpecification node)
            {
                base.ExplicitVisit(node);

                if (Changed) return;

                if (node.FromClause != null && node.FromClause.TableReferences.Count > 0 &&
                    node.WhereClause != null)
                {
                    var conds = new List<BooleanExpression>();
                    CollectAndConditions(node.WhereClause.SearchCondition, conds);

                    BooleanExpression? targetCond = null;
                    ExistsPredicate? targetExistsPredicate = null;
                    QuerySpecification? subquerySpec = null;
                    TableReference? subqueryTable = null;

                    foreach (var cond in conds)
                    {
                        ExistsPredicate? existsPredicate = null;
                        if (cond is ExistsPredicate ep)
                        {
                            existsPredicate = ep;
                        }
                        else if (cond is BooleanNotExpression notExpr && notExpr.Expression is ExistsPredicate nep)
                        {
                            existsPredicate = nep;
                        }

                        if (existsPredicate != null)
                        {
                            bool isNotExists = cond is BooleanNotExpression;
                            if (IsRewriteableExistsSubquery(node, existsPredicate, isNotExists, out subquerySpec, out subqueryTable))
                            {
                                targetCond = cond;
                                targetExistsPredicate = existsPredicate;
                                break;
                            }
                        }
                    }

                    if (targetCond != null && targetExistsPredicate != null && subquerySpec != null && subqueryTable != null)
                    {
                        bool isNotExists = targetCond is BooleanNotExpression;
                        ColumnReferenceExpression? isNullCol = null;

                        if (isNotExists)
                        {
                            var outerIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            CollectTableIdentifiers(node, outerIdentifiers);

                            var subqueryIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            CollectTableIdentifiers(subquerySpec, subqueryIdentifiers);

                            var subqueryConds = new List<BooleanExpression>();
                            CollectAndConditions(subquerySpec.WhereClause.SearchCondition, subqueryConds);

                            isNullCol = FindSubqueryColumnForIsNull(subqueryConds, outerIdentifiers, subqueryIdentifiers, subqueryTable, node);
                            if (isNullCol == null)
                            {
                                // Cannot find a subquery column to check for IS NULL, abort rewrite to be safe
                                return;
                            }
                        }

                        // 1. Rebuild WhereClause SearchCondition
                        conds.Remove(targetCond);

                        if (isNotExists && isNullCol != null)
                        {
                            var isNullExpr = new BooleanIsNullExpression
                            {
                                Expression = SqlNodeCloner.Clone(isNullCol) as ScalarExpression,
                                IsNot = false
                            };
                            conds.Add(isNullExpr);
                        }

                        if (conds.Count == 0)
                        {
                            node.WhereClause = null;
                        }
                        else if (conds.Count == 1)
                        {
                            node.WhereClause.SearchCondition = conds[0];
                        }
                        else
                        {
                            var newCondition = WrapInParenthesisIfNeeded(conds[0]);
                            for (int i = 1; i < conds.Count; i++)
                            {
                                newCondition = new BooleanBinaryExpression
                                {
                                    FirstExpression = newCondition,
                                    SecondExpression = WrapInParenthesisIfNeeded(conds[i]),
                                    BinaryExpressionType = BooleanBinaryExpressionType.And
                                };
                            }
                            node.WhereClause.SearchCondition = newCondition;
                        }

                        // 2. Append JOIN to the rightmost TableReference
                        var lastIdx = node.FromClause.TableReferences.Count - 1;
                        var lastRef = node.FromClause.TableReferences[lastIdx];

                        var joinType = isNotExists ? QualifiedJoinType.LeftOuter : QualifiedJoinType.Inner;

                        // Wrap subqueryTable in parentheses if it's a QualifiedJoin to format nicely
                        TableReference secondTableRef = (TableReference)SqlNodeCloner.Clone(subqueryTable)!;
                        if (secondTableRef is QualifiedJoin qj)
                        {
                            secondTableRef = new JoinParenthesisTableReference
                            {
                                Join = qj
                            };
                        }

                        var join = new QualifiedJoin
                        {
                            FirstTableReference = lastRef,
                            SecondTableReference = secondTableRef,
                            QualifiedJoinType = joinType,
                            SearchCondition = SqlNodeCloner.Clone(subquerySpec.WhereClause.SearchCondition) as BooleanExpression
                        };

                        node.FromClause.TableReferences[lastIdx] = join;

                        Changed = true;
                        var targetTable = GetTableString(subqueryTable);
                        var rewriteType = isNotExists ? "NOT EXISTS to LEFT JOIN" : "EXISTS to JOIN";
                        ChangeDetail = $"Converted {rewriteType} with {targetTable}";
                        _context.Log($"Converted {rewriteType} with {targetTable}");
                    }
                }
            }

            private HashSet<string> GetCorrelatedSubqueryPrefixes(
                IEnumerable<BooleanExpression> subqueryConds,
                HashSet<string> outerIdentifiers,
                HashSet<string> subqueryIdentifiers)
            {
                var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cond in subqueryConds)
                {
                    if (IsCorrelatedCondition(cond, outerIdentifiers, subqueryIdentifiers))
                    {
                        var visitor = new ColumnReferenceVisitor();
                        cond.Accept(visitor);
                        foreach (var colRef in visitor.Columns)
                        {
                            var mpi = colRef.MultiPartIdentifier;
                            if (mpi != null && mpi.Identifiers.Count > 1)
                            {
                                var prefix = mpi.Identifiers[0].Value;
                                if (subqueryIdentifiers.Contains(prefix))
                                {
                                    prefixes.Add(prefix);
                                }
                            }
                        }
                    }
                }
                return prefixes;
            }

            private ColumnReferenceExpression? FindSubqueryColumnForIsNull(
                IEnumerable<BooleanExpression> subqueryConds,
                HashSet<string> outerIdentifiers,
                HashSet<string> subqueryIdentifiers,
                TableReference subqueryTable,
                QuerySpecification outerQuery)
            {
                var correlatedSubqueryPrefixes = GetCorrelatedSubqueryPrefixes(subqueryConds, outerIdentifiers, subqueryIdentifiers);
                if (correlatedSubqueryPrefixes.Count == 0)
                {
                    var firstPrefix = GetFirstTablePrefix(subqueryTable);
                    if (firstPrefix != null)
                    {
                        correlatedSubqueryPrefixes.Add(firstPrefix);
                    }
                }

                // Phase 1a: Try to find a column belonging to a correlated subquery table
                foreach (var cond in subqueryConds)
                {
                    if (IsCorrelatedCondition(cond, outerIdentifiers, subqueryIdentifiers))
                    {
                        var visitor = new ColumnReferenceVisitor();
                        cond.Accept(visitor);

                        foreach (var colRef in visitor.Columns)
                        {
                            var mpi = colRef.MultiPartIdentifier;
                            if (mpi != null)
                            {
                                if (mpi.Identifiers.Count > 1)
                                {
                                    var prefix = mpi.Identifiers[0].Value;
                                    if (correlatedSubqueryPrefixes.Contains(prefix))
                                    {
                                        return colRef;
                                    }
                                }
                                else if (mpi.Identifiers.Count == 1)
                                {
                                    var qualifier = GetFirstTablePrefix(subqueryTable);
                                    if (qualifier != null && correlatedSubqueryPrefixes.Contains(qualifier))
                                    {
                                        var newMpi = new MultiPartIdentifier();
                                        newMpi.Identifiers.Add(new Identifier { Value = qualifier });
                                        newMpi.Identifiers.Add(new Identifier { Value = mpi.Identifiers[0].Value });
                                        return new ColumnReferenceExpression { MultiPartIdentifier = newMpi };
                                    }
                                }
                            }
                        }
                    }
                }

                // Phase 1b: Try to find any column belonging to the subquery table
                foreach (var cond in subqueryConds)
                {
                    if (IsCorrelatedCondition(cond, outerIdentifiers, subqueryIdentifiers))
                    {
                        var visitor = new ColumnReferenceVisitor();
                        cond.Accept(visitor);

                        foreach (var colRef in visitor.Columns)
                        {
                            var mpi = colRef.MultiPartIdentifier;
                            if (mpi != null)
                            {
                                if (mpi.Identifiers.Count > 1)
                                {
                                    var prefix = mpi.Identifiers[0].Value;
                                    if (subqueryIdentifiers.Contains(prefix))
                                    {
                                        return colRef;
                                    }
                                }
                                else if (mpi.Identifiers.Count == 1)
                                {
                                    var qualifier = GetFirstTablePrefix(subqueryTable);
                                    if (qualifier != null && subqueryIdentifiers.Contains(qualifier))
                                    {
                                        var newMpi = new MultiPartIdentifier();
                                        newMpi.Identifiers.Add(new Identifier { Value = qualifier });
                                        newMpi.Identifiers.Add(new Identifier { Value = mpi.Identifiers[0].Value });
                                        return new ColumnReferenceExpression { MultiPartIdentifier = newMpi };
                                    }
                                }
                            }
                        }
                    }
                }

                // Phase 2: Fallback to an outer query table column
                foreach (var cond in subqueryConds)
                {
                    if (IsCorrelatedCondition(cond, outerIdentifiers, subqueryIdentifiers))
                    {
                        var visitor = new ColumnReferenceVisitor();
                        cond.Accept(visitor);

                        foreach (var colRef in visitor.Columns)
                        {
                            var mpi = colRef.MultiPartIdentifier;
                            if (mpi != null)
                            {
                                if (mpi.Identifiers.Count > 1)
                                {
                                    var prefix = mpi.Identifiers[0].Value;
                                    if (outerIdentifiers.Contains(prefix))
                                    {
                                        return colRef;
                                    }
                                }
                                else if (mpi.Identifiers.Count == 1)
                                {
                                    var outerPrefix = GetFirstTablePrefix(outerQuery);
                                    if (outerPrefix != null)
                                    {
                                        var newMpi = new MultiPartIdentifier();
                                        newMpi.Identifiers.Add(new Identifier { Value = outerPrefix });
                                        newMpi.Identifiers.Add(new Identifier { Value = mpi.Identifiers[0].Value });
                                        return new ColumnReferenceExpression { MultiPartIdentifier = newMpi };
                                    }
                                }
                            }
                        }
                    }
                }

                return null;
            }
        }

        #region Helper Methods

        private static string? GetFirstTablePrefix(QuerySpecification query)
        {
            if (query.FromClause == null || query.FromClause.TableReferences.Count == 0)
                return null;

            var firstRef = query.FromClause.TableReferences[0];
            return GetFirstTablePrefix(firstRef);
        }

        private static string? GetFirstTablePrefix(TableReference tableRef)
        {
            if (tableRef is NamedTableReference namedTable)
            {
                return namedTable.Alias?.Value ?? namedTable.SchemaObject?.BaseIdentifier?.Value;
            }
            else if (tableRef is QualifiedJoin join)
            {
                return GetFirstTablePrefix(join.FirstTableReference);
            }
            else if (tableRef is JoinParenthesisTableReference jp)
            {
                return GetFirstTablePrefix(jp.Join);
            }
            return null;
        }

        private static BooleanExpression UnwrapParenthesis(BooleanExpression expr)
        {
            while (expr is BooleanParenthesisExpression paren)
            {
                expr = paren.Expression;
            }
            return expr;
        }

        private static void CollectAndConditions(BooleanExpression expr, List<BooleanExpression> list)
        {
            expr = UnwrapParenthesis(expr);
            if (expr is BooleanBinaryExpression binary && binary.BinaryExpressionType == BooleanBinaryExpressionType.And)
            {
                CollectAndConditions(binary.FirstExpression, list);
                CollectAndConditions(binary.SecondExpression, list);
            }
            else
            {
                list.Add(expr);
            }
        }

        private static BooleanExpression WrapInParenthesisIfNeeded(BooleanExpression expr)
        {
            if (expr is BooleanBinaryExpression binary && binary.BinaryExpressionType != BooleanBinaryExpressionType.And)
            {
                return new BooleanParenthesisExpression { Expression = expr };
            }
            if (expr is BooleanTernaryExpression || expr is BooleanNotExpression)
            {
                return new BooleanParenthesisExpression { Expression = expr };
            }
            return expr;
        }

        private static bool IsValidTableReferenceStructure(TableReference tableRef, out int namedTableCount)
        {
            namedTableCount = 0;
            if (tableRef is NamedTableReference)
            {
                namedTableCount = 1;
                return true;
            }
            else if (tableRef is QualifiedJoin qj)
            {
                if (IsValidTableReferenceStructure(qj.FirstTableReference, out int count1) &&
                    IsValidTableReferenceStructure(qj.SecondTableReference, out int count2))
                {
                    namedTableCount = count1 + count2;
                    return true;
                }
                return false;
            }
            else if (tableRef is JoinParenthesisTableReference jp)
            {
                return IsValidTableReferenceStructure(jp.Join, out namedTableCount);
            }
            return false;
        }

        private static bool IsRewriteableExistsSubquery(
            QuerySpecification outerQuery,
            ExistsPredicate existsPredicate,
            bool isNotExists,
            out QuerySpecification? subquerySpec,
            out TableReference? subqueryTable)
        {
            subquerySpec = null;
            subqueryTable = null;

            if (existsPredicate.Subquery == null) return false;

            if (existsPredicate.Subquery.QueryExpression is QuerySpecification spec)
            {
                if (spec.FromClause == null || spec.FromClause.TableReferences.Count != 1) return false;
                var tableRef = spec.FromClause.TableReferences[0];

                if (!IsValidTableReferenceStructure(tableRef, out int namedTableCount)) return false;

                if (namedTableCount < 1 || namedTableCount > 3) return false;
                if (namedTableCount > 1)
                {
                    var unwrapped = tableRef;
                    while (unwrapped is JoinParenthesisTableReference jp)
                    {
                        unwrapped = jp.Join;
                    }
                    if (unwrapped is not QualifiedJoin) return false;
                }

                if (spec.GroupByClause != null || spec.HavingClause != null) return false;
                if (spec.WhereClause == null) return false;

                var outerIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectTableIdentifiers(outerQuery, outerIdentifiers);

                var subqueryIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectTableIdentifiers(spec, subqueryIdentifiers);

                var subqueryConds = new List<BooleanExpression>();
                CollectAndConditions(spec.WhereClause.SearchCondition, subqueryConds);

                if (subqueryConds.Count == 0) return false;

                // At least one condition in the subquery must be a correlation condition
                bool hasCorrelation = false;
                foreach (var cond in subqueryConds)
                {
                    if (IsCorrelatedCondition(cond, outerIdentifiers, subqueryIdentifiers))
                    {
                        hasCorrelation = true;
                    }
                }

                if (!hasCorrelation) return false;

                subquerySpec = spec;
                subqueryTable = tableRef;
                return true;
            }

            return false;
        }

        private static bool IsCorrelatedCondition(BooleanExpression cond, HashSet<string> outerIdentifiers, HashSet<string> subqueryIdentifiers)
        {
            var visitor = new ColumnReferenceVisitor();
            cond.Accept(visitor);

            foreach (var colRef in visitor.Columns)
            {
                var mpi = colRef.MultiPartIdentifier;
                if (mpi != null && mpi.Identifiers.Count > 1)
                {
                    var prefix = mpi.Identifiers[0].Value;
                    if (outerIdentifiers.Contains(prefix) && !subqueryIdentifiers.Contains(prefix))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void CollectTableIdentifiers(QuerySpecification query, HashSet<string> identifiers)
        {
            if (query.FromClause == null) return;

            var visitor = new TableReferenceIdentifierVisitor();
            query.FromClause.Accept(visitor);

            foreach (var id in visitor.Identifiers)
            {
                identifiers.Add(id);
            }
        }

        private class TableReferenceIdentifierVisitor : TSqlFragmentVisitor
        {
            public List<string> Identifiers { get; } = new List<string>();

            public override void ExplicitVisit(NamedTableReference node)
            {
                if (node.SchemaObject?.BaseIdentifier != null)
                {
                    Identifiers.Add(node.SchemaObject.BaseIdentifier.Value);
                }
                if (node.Alias != null)
                {
                    Identifiers.Add(node.Alias.Value);
                }
                base.ExplicitVisit(node);
            }
        }

        private static string GetTableString(TableReference tableRef)
        {
            if (tableRef is NamedTableReference namedTable)
            {
                var schemaObj = namedTable.SchemaObject;
                var tableName = schemaObj != null ? string.Join(".", schemaObj.Identifiers.Select(i => i.Value)) : "OtherTable";
                if (namedTable.Alias != null)
                {
                    return $"{tableName} (as {namedTable.Alias.Value})";
                }
                return tableName;
            }
            else if (tableRef is QualifiedJoin qj)
            {
                return $"{GetTableString(qj.FirstTableReference)} JOIN {GetTableString(qj.SecondTableReference)}";
            }
            else if (tableRef is JoinParenthesisTableReference jp)
            {
                return GetTableString(jp.Join);
            }
            return "OtherTable";
        }

        private class ColumnReferenceVisitor : TSqlFragmentVisitor
        {
            public List<ColumnReferenceExpression> Columns { get; } = new List<ColumnReferenceExpression>();

            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                Columns.Add(node);
                base.ExplicitVisit(node);
            }
        }

        #endregion
    }
}
