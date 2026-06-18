using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;

namespace SqlXmlAnalyzer.Refactoring.Rules
{
    public class SubqueryToJoinRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_106_SUBQUERY_JOIN";
        public string Name => "Subquery to Join Optimizer";
        public string Description => "Rewrites simple non-correlated IN subqueries to INNER JOINs.";
        public int Priority => 60;

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
                var desc = visitor.ChangeDetail ?? "Converted IN subquery to JOIN";
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
                        if (cond is InPredicate inPredicate && IsRewriteableInSubquery(inPredicate, out _, out _, out _))
                        {
                            Found = true;
                            return;
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

                    InPredicate? targetInPredicate = null;
                    QuerySpecification? subquerySpec = null;
                    TableReference? subqueryTable = null;
                    ScalarExpression? subqueryProjectedExpr = null;

                    foreach (var cond in conds)
                    {
                        if (cond is InPredicate inPredicate &&
                            IsRewriteableInSubquery(inPredicate, out subquerySpec, out subqueryTable, out subqueryProjectedExpr))
                        {
                            targetInPredicate = inPredicate;
                            break;
                        }
                    }

                    if (targetInPredicate != null && subquerySpec != null && subqueryTable != null && subqueryProjectedExpr != null)
                    {
                        // 1. Rebuild WhereClause SearchCondition
                        conds.Remove(targetInPredicate);
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
                        
                        var joinCondition = new BooleanComparisonExpression
                        {
                            ComparisonType = BooleanComparisonType.Equals,
                            FirstExpression = SqlNodeCloner.Clone(targetInPredicate.Expression) as ScalarExpression,
                            SecondExpression = SqlNodeCloner.Clone(subqueryProjectedExpr) as ScalarExpression
                        };

                        BooleanExpression finalSearchCondition = joinCondition;
                        if (subquerySpec.WhereClause != null)
                        {
                            finalSearchCondition = new BooleanBinaryExpression
                            {
                                FirstExpression = WrapInParenthesisIfNeeded(joinCondition),
                                SecondExpression = WrapInParenthesisIfNeeded((BooleanExpression)SqlNodeCloner.Clone(subquerySpec.WhereClause.SearchCondition)!),
                                BinaryExpressionType = BooleanBinaryExpressionType.And
                            };
                        }

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
                            QualifiedJoinType = QualifiedJoinType.Inner,
                            SearchCondition = finalSearchCondition
                        };

                        node.FromClause.TableReferences[lastIdx] = join;

                        Changed = true;
                        var sourceCol = GetExpressionString(targetInPredicate.Expression);
                        var targetTable = GetTableString(subqueryTable);
                        ChangeDetail = $"Converted IN subquery on {sourceCol} to JOIN with {targetTable}";
                        
                        _context.Log($"Converted IN subquery on {sourceCol} to JOIN with {targetTable}");
                    }
                }
            }
        }

        #region Helper Methods

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

        private static bool IsRewriteableInSubquery(
            InPredicate inPredicate,
            out QuerySpecification? subquerySpec,
            out TableReference? subqueryTable,
            out ScalarExpression? subqueryProjectedExpr)
        {
            subquerySpec = null;
            subqueryTable = null;
            subqueryProjectedExpr = null;

            if (inPredicate.NotDefined) return false;
            if (inPredicate.Subquery == null) return false;

            if (inPredicate.Subquery.QueryExpression is QuerySpecification spec)
            {
                // Must select exactly one column/expression
                if (spec.SelectElements.Count != 1) return false;
                if (spec.SelectElements[0] is not SelectScalarExpression scalarExpr) return false;

                // Subquery FROM must have exactly one table reference, which can be a NamedTableReference or QualifiedJoin
                if (spec.FromClause == null || spec.FromClause.TableReferences.Count != 1) return false;
                var tableRef = spec.FromClause.TableReferences[0];
                
                if (!IsValidTableReferenceStructure(tableRef, out int namedTableCount)) return false;
                if (namedTableCount < 1 || namedTableCount > 2) return false;
                if (namedTableCount == 2)
                {
                    var unwrapped = tableRef;
                    while (unwrapped is JoinParenthesisTableReference jp)
                    {
                        unwrapped = jp.Join;
                    }
                    if (unwrapped is not QualifiedJoin) return false;
                }

                // GroupBy and Having are not allowed in simple subquery conversion
                if (spec.GroupByClause != null || spec.HavingClause != null) return false;

                subquerySpec = spec;
                subqueryTable = tableRef;
                subqueryProjectedExpr = scalarExpr.Expression;
                return true;
            }

            return false;
        }

        private static string GetExpressionString(ScalarExpression expr)
        {
            if (expr is ColumnReferenceExpression colRef)
            {
                var mpi = colRef.MultiPartIdentifier;
                if (mpi != null)
                {
                    return string.Join(".", mpi.Identifiers.Select(i => i.Value));
                }
            }
            return "column";
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
