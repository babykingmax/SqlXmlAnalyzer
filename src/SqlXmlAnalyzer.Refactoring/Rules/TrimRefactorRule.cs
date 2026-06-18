using System;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;

namespace SqlXmlAnalyzer.Refactoring.Rules
{
    public class TrimRefactorRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_103_TRIM";
        public string Name => "Trim Function Optimizer";
        public string Description => "Rewrites LTRIM(RTRIM(Column)) = 'xxx' to Column = 'xxx'. Removing RTRIM is always safe, LTRIM removal assumes no leading space values.";
        public int Priority => 30;

        public bool CanApply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new FinderVisitor();
            fragment.Accept(visitor);
            return visitor.Found;
        }

        public RuleResult Apply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new RewriteVisitor(context);
            if (fragment is BooleanExpression boolExpr)
            {
                var replaced = visitor.Rewrite(boolExpr);
                if (replaced != boolExpr)
                {
                    var desc = visitor.ChangeDetail ?? "Optimized LTRIM/RTRIM comparison";
                    return new RuleResult(replaced, true, desc);
                }
            }
            else
            {
                fragment.Accept(visitor);
                if (visitor.Changed)
                {
                    var desc = visitor.ChangeDetail ?? "Optimized LTRIM/RTRIM comparison";
                    return new RuleResult(fragment, true, desc);
                }
            }
            return new RuleResult(fragment, false, null);
        }

        private class FinderVisitor : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                if (node.ComparisonType == BooleanComparisonType.Equals || 
                    node.ComparisonType == BooleanComparisonType.NotEqualToBrackets || 
                    node.ComparisonType == BooleanComparisonType.NotEqualToExclamation)
                {
                    if (IsOptimizeable(node.FirstExpression, node.SecondExpression) ||
                        IsOptimizeable(node.SecondExpression, node.FirstExpression))
                    {
                        Found = true;
                        return;
                    }
                }
                base.ExplicitVisit(node);
            }
        }

        private class RewriteVisitor : SqlXmlAnalyzer.Core.Refactoring.BooleanExpressionReplacementVisitor
        {
            public string? ChangeDetail { get; private set; }

            public RewriteVisitor(RefactorContext context) : base(context)
            {
            }

            public BooleanExpression Rewrite(BooleanExpression expr)
            {
                return ReplaceExpression(expr);
            }

            protected override BooleanExpression ReplaceExpression(BooleanExpression expression)
            {
                if (expression is BooleanComparisonExpression compExpr && 
                    (compExpr.ComparisonType == BooleanComparisonType.Equals || 
                     compExpr.ComparisonType == BooleanComparisonType.NotEqualToBrackets || 
                     compExpr.ComparisonType == BooleanComparisonType.NotEqualToExclamation))
                {
                    if (TryOptimize(compExpr.FirstExpression, compExpr.SecondExpression, compExpr.ComparisonType, out var optimized))
                    {
                        return optimized;
                    }
                    if (TryOptimize(compExpr.SecondExpression, compExpr.FirstExpression, compExpr.ComparisonType, out var optimizedReversed))
                    {
                        return optimizedReversed;
                    }
                }
                return expression;
            }

            private bool TryOptimize(ScalarExpression left, ScalarExpression right, BooleanComparisonType compType, out BooleanExpression optimized)
            {
                optimized = null!;
                if (TryStripTrims(left, out var colRef, out bool hasLTrim, out bool hasRTrim))
                {
                    if (right is StringLiteral strLit)
                    {
                        // Ensure literal doesn't have leading/trailing spaces that would break equivalence
                        if (strLit.Value.Length > 0 && 
                            !char.IsWhiteSpace(strLit.Value[0]) && 
                            !char.IsWhiteSpace(strLit.Value[strLit.Value.Length - 1]))
                        {
                            optimized = new BooleanComparisonExpression
                            {
                                ComparisonType = compType,
                                FirstExpression = colRef,
                                SecondExpression = right
                            };
                            var colName = GetColumnName(colRef!);
                            if (hasLTrim)
                            {
                                _context.Warn($"Removed LTRIM on column {colName}. Note: This assumes column values do not contain leading spaces.");
                            }
                            _context.Log($"Optimized LTRIM/RTRIM on column {colName}");
                            ChangeDetail = $"Optimized LTRIM/RTRIM comparison on column {colName}";
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        private static bool IsOptimizeable(ScalarExpression expr, ScalarExpression comparison)
        {
            if (TryStripTrims(expr, out _, out _, out _))
            {
                if (comparison is StringLiteral strLit)
                {
                    return strLit.Value.Length > 0 && 
                           !char.IsWhiteSpace(strLit.Value[0]) && 
                           !char.IsWhiteSpace(strLit.Value[strLit.Value.Length - 1]);
                }
            }
            return false;
        }

        private static bool TryStripTrims(ScalarExpression expr, out ColumnReferenceExpression? colRef, out bool hasLTrim, out bool hasRTrim)
        {
            colRef = null;
            hasLTrim = false;
            hasRTrim = false;

            ScalarExpression current = expr;
            while (true)
            {
                if (current is FunctionCall funcCall)
                {
                    var name = funcCall.FunctionName?.Value;
                    if (string.Equals(name, "LTRIM", StringComparison.OrdinalIgnoreCase) && funcCall.Parameters.Count == 1)
                    {
                        hasLTrim = true;
                        current = funcCall.Parameters[0];
                    }
                    else if (string.Equals(name, "RTRIM", StringComparison.OrdinalIgnoreCase) && funcCall.Parameters.Count == 1)
                    {
                        hasRTrim = true;
                        current = funcCall.Parameters[0];
                    }
                    else if (string.Equals(name, "TRIM", StringComparison.OrdinalIgnoreCase) && funcCall.Parameters.Count == 1)
                    {
                        hasLTrim = true;
                        hasRTrim = true;
                        current = funcCall.Parameters[0];
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            if (current is ColumnReferenceExpression col)
            {
                colRef = col;
                return hasLTrim || hasRTrim;
            }

            return false;
        }

        private static string GetColumnName(ColumnReferenceExpression colRef)
        {
            var mpi = colRef.MultiPartIdentifier;
            if (mpi != null && mpi.Identifiers.Count > 0)
            {
                return mpi.Identifiers[mpi.Identifiers.Count - 1].Value;
            }
            return "Column";
        }
    }
}
