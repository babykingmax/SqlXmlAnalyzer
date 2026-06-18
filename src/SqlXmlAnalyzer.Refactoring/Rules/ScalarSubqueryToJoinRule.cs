using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;

namespace SqlXmlAnalyzer.Refactoring.Rules
{
    public class ScalarSubqueryToJoinRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_107_SCALAR_SUBQUERY_JOIN";
        public string Name => "Scalar Subquery to Join Optimizer";
        public string Description => "Rewrites correlated scalar subqueries in the SELECT list containing aggregate functions (COUNT, SUM, MAX, MIN, AVG) into LEFT JOIN + GROUP BY to avoid subquery execution overhead.";
        public int Priority => 80;

        public bool CanApply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new FinderVisitor();
            fragment.Accept(visitor);
            return visitor.Found;
        }

        public RuleResult Apply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new RewriteVisitor(context, RuleId);
            fragment.Accept(visitor);

            if (visitor.Changed)
            {
                var desc = visitor.ChangeDetail ?? "Converted correlated scalar subquery to LEFT JOIN";
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

                if (node.FromClause != null && node.FromClause.TableReferences.Count > 0)
                {
                    var mainQueryAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var tableRef in node.FromClause.TableReferences)
                    {
                        CollectTableAliases(tableRef, mainQueryAliases);
                    }

                    foreach (var element in node.SelectElements)
                    {
                        if (element is SelectScalarExpression selectExpr &&
                            IsRewriteableScalarSubquery(selectExpr, mainQueryAliases, out _, out _, out _, out _, out _, out _, out _))
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
            private readonly string _ruleId;
            private int _subqueryCounter = 0;

            public bool Changed { get; private set; }
            public string? ChangeDetail { get; private set; }

            public RewriteVisitor(RefactorContext context, string ruleId)
            {
                _context = context;
                _ruleId = ruleId;
            }

            public override void ExplicitVisit(QuerySpecification node)
            {
                // Visit nested queries first
                base.ExplicitVisit(node);

                if (node.FromClause == null || node.FromClause.TableReferences.Count == 0)
                {
                    return;
                }

                var mainQueryAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tableRef in node.FromClause.TableReferences)
                {
                    CollectTableAliases(tableRef, mainQueryAliases);
                }

                bool specChanged = false;
                var detailsList = new List<string>();

                for (int i = 0; i < node.SelectElements.Count; i++)
                {
                    var element = node.SelectElements[i];
                    if (element is SelectScalarExpression selectExpr &&
                        IsRewriteableScalarSubquery(selectExpr, mainQueryAliases, out var scalarSub, out var subQuerySpec, out var subQueryTable, out var aggFunc, out var subQueryAliases, out var correlatedConditions, out var nonCorrelatedConditions))
                    {
                        // 1. Generate unique aliases
                        string subqueryTableAlias = $"t_sub_{_subqueryCounter}";
                        string aggAlias = $"agg_{_subqueryCounter}";
                        _subqueryCounter++;

                        // 2. Map internal columns
                        var internalColNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var selectColsMap = new Dictionary<string, ColumnReferenceExpression>(StringComparer.OrdinalIgnoreCase);

                        foreach (var cond in correlatedConditions)
                        {
                            if (IsCorrelatedEqualsCondition(cond, subQueryAliases, mainQueryAliases, out var subCol, out _))
                            {
                                string colName = GetColumnName(subCol!);
                                if (!string.IsNullOrEmpty(colName) && !internalColNames.Contains(colName))
                                {
                                    internalColNames.Add(colName);
                                    selectColsMap[colName] = subCol!;
                                }
                            }
                        }

                        // 3. Build derived table SELECT list
                        var derivedSelectElements = new List<SelectElement>();
                        foreach (var colName in internalColNames)
                        {
                            var internalCol = selectColsMap[colName];
                            var lastIdent = internalCol.MultiPartIdentifier.Identifiers.Last();
                            derivedSelectElements.Add(new SelectScalarExpression
                            {
                                Expression = SqlNodeCloner.Clone(internalCol) as ScalarExpression,
                                ColumnName = new IdentifierOrValueExpression
                                {
                                    Identifier = SqlNodeCloner.CloneIdentifier(lastIdent)
                                }
                            });
                        }

                        // Add aggregate function
                        derivedSelectElements.Add(new SelectScalarExpression
                        {
                            Expression = SqlNodeCloner.Clone(aggFunc) as ScalarExpression,
                            ColumnName = new IdentifierOrValueExpression
                            {
                                Identifier = new Identifier { Value = aggAlias }
                            }
                        });

                        // 4. Build derived table GROUP BY
                        var derivedGroupByClause = new GroupByClause();
                        foreach (var colName in internalColNames)
                        {
                            var internalCol = selectColsMap[colName];
                            derivedGroupByClause.GroupingSpecifications.Add(new ExpressionGroupingSpecification
                            {
                                Expression = SqlNodeCloner.Clone(internalCol) as ScalarExpression
                            });
                        }

                        // 5. Build derived table WHERE clause
                        WhereClause? derivedWhereClause = null;
                        if (nonCorrelatedConditions.Count > 0)
                        {
                            var newCondition = WrapInParenthesisIfNeeded(nonCorrelatedConditions[0]);
                            for (int k = 1; k < nonCorrelatedConditions.Count; k++)
                            {
                                newCondition = new BooleanBinaryExpression
                                {
                                    FirstExpression = newCondition,
                                    SecondExpression = WrapInParenthesisIfNeeded(nonCorrelatedConditions[k]),
                                    BinaryExpressionType = BooleanBinaryExpressionType.And
                                };
                            }
                            derivedWhereClause = new WhereClause { SearchCondition = newCondition };
                        }

                        // 6. Assemble derived table QuerySpecification
                        var derivedQuerySpec = new QuerySpecification
                        {
                            FromClause = new FromClause(),
                            GroupByClause = derivedGroupByClause,
                            WhereClause = derivedWhereClause
                        };
                        derivedQuerySpec.FromClause.TableReferences.Add(SqlNodeCloner.Clone(subQueryTable) as TableReference);
                        foreach (var elem in derivedSelectElements)
                        {
                            derivedQuerySpec.SelectElements.Add(elem);
                        }

                        var derivedTable = new QueryDerivedTable
                        {
                            QueryExpression = derivedQuerySpec,
                            Alias = new Identifier { Value = subqueryTableAlias }
                        };

                        // 7. Construct QualifiedJoin ON search condition
                        BooleanExpression? joinCondition = null;
                        foreach (var cond in correlatedConditions)
                        {
                            if (IsCorrelatedEqualsCondition(cond, subQueryAliases, mainQueryAliases, out var subCol, out var mainCol))
                            {
                                var lastIdent = subCol!.MultiPartIdentifier.Identifiers.Last();
                                var derivedColRef = new ColumnReferenceExpression
                                {
                                    MultiPartIdentifier = new MultiPartIdentifier
                                    {
                                        Identifiers =
                                        {
                                            new Identifier { Value = subqueryTableAlias },
                                            SqlNodeCloner.CloneIdentifier(lastIdent)!
                                        }
                                    }
                                };

                                var singleOnCond = new BooleanComparisonExpression
                                {
                                    ComparisonType = BooleanComparisonType.Equals,
                                    FirstExpression = derivedColRef,
                                    SecondExpression = SqlNodeCloner.Clone(mainCol) as ScalarExpression
                                };

                                if (joinCondition == null)
                                {
                                    joinCondition = singleOnCond;
                                }
                                else
                                {
                                    joinCondition = new BooleanBinaryExpression
                                    {
                                        FirstExpression = WrapInParenthesisIfNeeded(joinCondition),
                                        SecondExpression = WrapInParenthesisIfNeeded(singleOnCond),
                                        BinaryExpressionType = BooleanBinaryExpressionType.And
                                    };
                                }
                            }
                        }

                        // 8. Add QualifiedJoin to outer FROM clause
                        var lastIdx = node.FromClause.TableReferences.Count - 1;
                        var lastRef = node.FromClause.TableReferences[lastIdx];

                        var joinNode = new QualifiedJoin
                        {
                            FirstTableReference = lastRef,
                            SecondTableReference = derivedTable,
                            QualifiedJoinType = QualifiedJoinType.LeftOuter,
                            SearchCondition = joinCondition
                        };

                        node.FromClause.TableReferences[lastIdx] = joinNode;

                        // 9. Replace subquery in outer SELECT list
                        ScalarExpression replaceExpr = new ColumnReferenceExpression
                        {
                            MultiPartIdentifier = new MultiPartIdentifier
                            {
                                Identifiers =
                                {
                                    new Identifier { Value = subqueryTableAlias },
                                    new Identifier { Value = aggAlias }
                                }
                            }
                        };

                        if (aggFunc!.FunctionName.Value.Equals("COUNT", StringComparison.OrdinalIgnoreCase))
                        {
                            var isnullCall = new FunctionCall
                            {
                                FunctionName = new Identifier { Value = "ISNULL" }
                            };
                            isnullCall.Parameters.Add(replaceExpr);
                            isnullCall.Parameters.Add(new IntegerLiteral { Value = "0" });
                            replaceExpr = isnullCall;
                        }

                        selectExpr.Expression = replaceExpr;

                        specChanged = true;
                        string tableDesc = GetTableString(subQueryTable);
                        detailsList.Add($"Converted subquery on {tableDesc} to LEFT JOIN");
                        _context.Log($"[{_ruleId}] Converted subquery on {tableDesc} to LEFT JOIN ({subqueryTableAlias})");
                    }
                }

                if (specChanged)
                {
                    Changed = true;
                    ChangeDetail = string.Join("; ", detailsList);
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

        private static void CollectTableAliases(TableReference? tableRef, HashSet<string> aliases)
        {
            if (tableRef == null) return;

            if (tableRef is TableReferenceWithAlias tableWithAlias)
            {
                if (tableWithAlias.Alias != null)
                {
                    aliases.Add(tableWithAlias.Alias.Value.ToUpperInvariant());
                }
                else if (tableWithAlias is NamedTableReference namedTable)
                {
                    if (namedTable.SchemaObject != null && namedTable.SchemaObject.BaseIdentifier != null)
                    {
                        aliases.Add(namedTable.SchemaObject.BaseIdentifier.Value.ToUpperInvariant());
                    }
                }
                else if (tableWithAlias is VariableTableReference varTable)
                {
                    if (varTable.Variable != null)
                    {
                        aliases.Add(varTable.Variable.Name.ToUpperInvariant());
                    }
                }
                else if (tableWithAlias is SchemaObjectFunctionTableReference functionTable)
                {
                    if (functionTable.SchemaObject != null && functionTable.SchemaObject.BaseIdentifier != null)
                    {
                        aliases.Add(functionTable.SchemaObject.BaseIdentifier.Value.ToUpperInvariant());
                    }
                }
            }
            else if (tableRef is QualifiedJoin qualifiedJoin)
            {
                CollectTableAliases(qualifiedJoin.FirstTableReference, aliases);
                CollectTableAliases(qualifiedJoin.SecondTableReference, aliases);
            }
            else if (tableRef is UnqualifiedJoin unqualifiedJoin)
            {
                CollectTableAliases(unqualifiedJoin.FirstTableReference, aliases);
                CollectTableAliases(unqualifiedJoin.SecondTableReference, aliases);
            }
            else if (tableRef is JoinParenthesisTableReference parenTable)
            {
                CollectTableAliases(parenTable.Join, aliases);
            }
        }

        private static bool IsRewriteableScalarSubquery(
            SelectScalarExpression selectExpr,
            HashSet<string> mainQueryAliases,
            out ScalarSubquery? scalarSub,
            out QuerySpecification? subQuerySpec,
            out NamedTableReference? subQueryTable,
            out FunctionCall? aggFunc,
            out HashSet<string> subQueryAliases,
            out List<BooleanExpression> correlatedConditions,
            out List<BooleanExpression> nonCorrelatedConditions)
        {
            scalarSub = null;
            subQuerySpec = null;
            subQueryTable = null;
            aggFunc = null;
            subQueryAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            correlatedConditions = new List<BooleanExpression>();
            nonCorrelatedConditions = new List<BooleanExpression>();

            if (selectExpr.Expression is not ScalarSubquery sub)
            {
                return false;
            }

            if (sub.QueryExpression is not QuerySpecification subSpec)
            {
                return false;
            }

            // 1. SELECT list must have exactly 1 expression which is a supported aggregate function
            if (subSpec.SelectElements.Count != 1)
            {
                return false;
            }

            if (subSpec.SelectElements[0] is not SelectScalarExpression subSelectExpr)
            {
                return false;
            }

            if (subSelectExpr.Expression is not FunctionCall func)
            {
                return false;
            }

            string funcName = func.FunctionName.Value.ToUpperInvariant();
            if (funcName != "COUNT" && funcName != "SUM" && funcName != "MAX" && funcName != "MIN" && funcName != "AVG")
            {
                return false;
            }

            if (func.OverClause != null)
            {
                return false;
            }

            // Check for outer reference leakage in aggregate function parameters
            var paramVisitor = new ColumnReferenceVisitor();
            foreach (var param in func.Parameters)
            {
                param.Accept(paramVisitor);
            }
            foreach (var col in paramVisitor.Columns)
            {
                if (ReferencesAlias(col, mainQueryAliases))
                {
                    return false;
                }
            }

            // 2. Simple structure check
            if (subSpec.GroupByClause != null || subSpec.HavingClause != null || subSpec.OrderByClause != null || subSpec.TopRowFilter != null)
            {
                return false;
            }

            if (subSpec.FromClause == null || subSpec.FromClause.TableReferences.Count != 1)
            {
                return false;
            }

            if (subSpec.FromClause.TableReferences[0] is not NamedTableReference namedTable)
            {
                return false;
            }

            // 3. Collect subquery aliases
            if (namedTable.Alias != null)
            {
                subQueryAliases.Add(namedTable.Alias.Value);
            }
            else if (namedTable.SchemaObject != null && namedTable.SchemaObject.BaseIdentifier != null)
            {
                subQueryAliases.Add(namedTable.SchemaObject.BaseIdentifier.Value);
            }

            // 4. Extract conditions
            if (subSpec.WhereClause == null || subSpec.WhereClause.SearchCondition == null)
            {
                return false;
            }

            var allConditions = new List<BooleanExpression>();
            CollectAndConditions(subSpec.WhereClause.SearchCondition, allConditions);

            foreach (var cond in allConditions)
            {
                if (IsCorrelatedEqualsCondition(cond, subQueryAliases, mainQueryAliases, out _, out _))
                {
                    correlatedConditions.Add(cond);
                }
                else
                {
                    // Check for outer reference leakage in non-correlated conditions
                    var condVisitor = new ColumnReferenceVisitor();
                    cond.Accept(condVisitor);
                    foreach (var col in condVisitor.Columns)
                    {
                        if (ReferencesAlias(col, mainQueryAliases))
                        {
                            return false;
                        }
                    }
                    nonCorrelatedConditions.Add(cond);
                }
            }

            if (correlatedConditions.Count == 0)
            {
                return false;
            }

            foreach (var cond in nonCorrelatedConditions)
            {
                var unwrapped = UnwrapParenthesis(cond);
                if (unwrapped is BooleanComparisonExpression comp)
                {
                    if (comp.FirstExpression is ColumnReferenceExpression leftCol &&
                        comp.SecondExpression is ColumnReferenceExpression rightCol)
                    {
                        bool leftIsSub = ReferencesAlias(leftCol, subQueryAliases) || (leftCol.MultiPartIdentifier.Identifiers.Count == 1);
                        bool rightIsSub = ReferencesAlias(rightCol, subQueryAliases) || (rightCol.MultiPartIdentifier.Identifiers.Count == 1);

                        bool leftIsUnqualified = leftCol.MultiPartIdentifier.Identifiers.Count == 1;
                        bool rightIsUnqualified = rightCol.MultiPartIdentifier.Identifiers.Count == 1;

                        if (leftIsSub && !ReferencesAlias(rightCol, subQueryAliases) && rightIsUnqualified)
                        {
                            return false;
                        }
                        if (rightIsSub && !ReferencesAlias(leftCol, subQueryAliases) && leftIsUnqualified)
                        {
                            return false;
                        }
                        if (leftIsUnqualified && rightIsUnqualified)
                        {
                            return false;
                        }
                    }
                }
            }

            scalarSub = sub;
            subQuerySpec = subSpec;
            subQueryTable = namedTable;
            aggFunc = func;
            return true;
        }

        private static bool IsCorrelatedEqualsCondition(
            BooleanExpression expr,
            HashSet<string> subQueryAliases,
            HashSet<string> mainQueryAliases,
            out ColumnReferenceExpression? subCol,
            out ColumnReferenceExpression? mainCol)
        {
            subCol = null;
            mainCol = null;

            expr = UnwrapParenthesis(expr);
            if (expr is not BooleanComparisonExpression comp || comp.ComparisonType != BooleanComparisonType.Equals)
            {
                return false;
            }

            if (comp.FirstExpression is not ColumnReferenceExpression left ||
                comp.SecondExpression is not ColumnReferenceExpression right)
            {
                return false;
            }

            bool leftHasMultiplier = left.MultiPartIdentifier != null && left.MultiPartIdentifier.Identifiers.Count > 1;
            bool rightHasMultiplier = right.MultiPartIdentifier != null && right.MultiPartIdentifier.Identifiers.Count > 1;

            bool leftIsMain = ReferencesAlias(left, mainQueryAliases) && !ReferencesAlias(left, subQueryAliases);
            bool rightIsMain = ReferencesAlias(right, mainQueryAliases) && !ReferencesAlias(right, subQueryAliases);

            bool leftIsSub = ReferencesAlias(left, subQueryAliases) || (!leftHasMultiplier && !leftIsMain);
            bool rightIsSub = ReferencesAlias(right, subQueryAliases) || (!rightHasMultiplier && !rightIsMain);

            if (leftIsSub && rightIsMain)
            {
                subCol = left;
                mainCol = right;
                return true;
            }

            if (rightIsSub && leftIsMain)
            {
                subCol = right;
                mainCol = left;
                return true;
            }

            return false;
        }

        private static bool ReferencesAlias(ColumnReferenceExpression colRef, HashSet<string> aliases)
        {
            if (colRef.MultiPartIdentifier == null || colRef.MultiPartIdentifier.Identifiers.Count <= 1)
            {
                return false;
            }

            for (int i = 0; i < colRef.MultiPartIdentifier.Identifiers.Count - 1; i++)
            {
                if (aliases.Contains(colRef.MultiPartIdentifier.Identifiers[i].Value))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetColumnName(ColumnReferenceExpression colRef)
        {
            if (colRef.MultiPartIdentifier != null && colRef.MultiPartIdentifier.Identifiers.Count > 0)
            {
                return colRef.MultiPartIdentifier.Identifiers[colRef.MultiPartIdentifier.Identifiers.Count - 1].Value;
            }
            return string.Empty;
        }

        private static string GetTableString(NamedTableReference? tableRef)
        {
            if (tableRef == null) return "Table";
            if (tableRef.Alias != null) return tableRef.Alias.Value;
            if (tableRef.SchemaObject != null && tableRef.SchemaObject.BaseIdentifier != null)
            {
                return tableRef.SchemaObject.BaseIdentifier.Value;
            }
            return "Table";
        }

        #endregion

        #region Cloner Utility

        // Obsolete inner cloner deleted. Using public SqlNodeCloner instead.

        #endregion

        private class ColumnReferenceVisitor : TSqlFragmentVisitor
        {
            public List<ColumnReferenceExpression> Columns { get; } = new List<ColumnReferenceExpression>();

            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                Columns.Add(node);
                base.ExplicitVisit(node);
            }
        }
    }
}
