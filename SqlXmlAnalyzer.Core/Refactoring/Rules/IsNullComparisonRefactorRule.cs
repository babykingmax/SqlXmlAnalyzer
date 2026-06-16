using System;
using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring.Rules
{
    public class IsNullComparisonRefactorRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_101_ISNULL_EQUAL";
        public string Name => "IsNull Equal Comparison Optimizer";
        public string Description => "Rewrites ISNULL(Column, DefaultValue) = Value to Column = Value when Value != DefaultValue.";
        public int Priority => 10;

        public bool CanApply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new IsNullFinderVisitor();
            fragment.Accept(visitor);
            return visitor.Found;
        }

        public TSqlFragment Apply(TSqlFragment fragment, RefactorContext context)
        {
            if (fragment is BooleanExpression boolExpr)
            {
                var visitor = new IsNullRewriteVisitor(context);
                var replaced = visitor.Rewrite(boolExpr);
                if (replaced != boolExpr) return replaced;
            }
            else
            {
                var visitor = new IsNullRewriteVisitor(context);
                fragment.Accept(visitor);
            }
            return fragment;
        }

        private class IsNullFinderVisitor : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                if (IsOptimizeable(node.FirstExpression, node.SecondExpression, node.ComparisonType) ||
                    IsOptimizeable(node.SecondExpression, node.FirstExpression, GetInverseComparisonType(node.ComparisonType)))
                {
                    Found = true;
                    return;
                }
                base.ExplicitVisit(node);
            }
        }

        private class IsNullRewriteVisitor : BooleanExpressionReplacementVisitor
        {
            public IsNullRewriteVisitor(RefactorContext context) : base(context)
            {
            }

            public BooleanExpression Rewrite(BooleanExpression expr)
            {
                return ReplaceExpression(expr);
            }

            protected override BooleanExpression ReplaceExpression(BooleanExpression expression)
            {
                if (expression is BooleanComparisonExpression compExpr)
                {
                    if (TryOptimize(compExpr.FirstExpression, compExpr.SecondExpression, compExpr.ComparisonType, out var optimized))
                    {
                        return optimized;
                    }
                    if (TryOptimize(compExpr.SecondExpression, compExpr.FirstExpression, GetInverseComparisonType(compExpr.ComparisonType), out var optimizedReversed))
                    {
                        return optimizedReversed;
                    }
                }
                return expression;
            }

            private bool TryOptimize(ScalarExpression left, ScalarExpression right, BooleanComparisonType compType, out BooleanExpression optimized)
            {
                optimized = null!;
                if (TryGetIsNullPattern(left, out var colRef, out var defaultValue))
                {
                    if (colRef != null && IsLiteralOrConstant(defaultValue) && IsLiteralOrConstant(right))
                    {
                        if (TryEvaluateComparison(defaultValue, right, compType, out bool satisfies))
                        {
                            if (!satisfies)
                            {
                                optimized = new BooleanParenthesisExpression
                                {
                                    Expression = new BooleanBinaryExpression
                                    {
                                        BinaryExpressionType = BooleanBinaryExpressionType.And,
                                        FirstExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = compType,
                                            FirstExpression = colRef,
                                            SecondExpression = right
                                        },
                                        SecondExpression = new BooleanIsNullExpression
                                        {
                                            Expression = colRef,
                                            IsNot = true
                                        }
                                    }
                                };
                                _context.Log($"Optimized ISNULL comparison on column {GetColumnName(colRef)} (not satisfied by default value)");
                                return true;
                            }
                            else
                            {
                                optimized = new BooleanParenthesisExpression
                                {
                                    Expression = new BooleanBinaryExpression
                                    {
                                        BinaryExpressionType = BooleanBinaryExpressionType.Or,
                                        FirstExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = compType,
                                            FirstExpression = colRef,
                                            SecondExpression = right
                                        },
                                        SecondExpression = new BooleanIsNullExpression
                                        {
                                            Expression = colRef,
                                            IsNot = false
                                        }
                                    }
                                };
                                _context.Log($"Optimized ISNULL comparison on column {GetColumnName(colRef)} (satisfied by default value)");
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
        }

        private static bool IsOptimizeable(ScalarExpression expr, ScalarExpression comparison, BooleanComparisonType compType)
        {
            if (TryGetIsNullPattern(expr, out _, out var defaultValue))
            {
                if (IsLiteralOrConstant(defaultValue) && IsLiteralOrConstant(comparison))
                {
                    return TryEvaluateComparison(defaultValue, comparison, compType, out _);
                }
            }
            return false;
        }

        private static bool TryGetIsNullPattern(ScalarExpression expr, out ColumnReferenceExpression? colRef, out ScalarExpression? defaultValue)
        {
            colRef = null;
            defaultValue = null;

            if (expr is FunctionCall funcCall &&
                string.Equals(funcCall.FunctionName?.Value, "ISNULL", StringComparison.OrdinalIgnoreCase) &&
                funcCall.Parameters.Count == 2)
            {
                if (funcCall.Parameters[0] is ColumnReferenceExpression col)
                {
                    colRef = col;
                    defaultValue = funcCall.Parameters[1];
                    return true;
                }
            }
            else if (expr is CoalesceExpression coalesce && coalesce.Expressions.Count == 2)
            {
                if (coalesce.Expressions[0] is ColumnReferenceExpression col)
                {
                    colRef = col;
                    defaultValue = coalesce.Expressions[1];
                    return true;
                }
            }
            return false;
        }

        private static bool IsLiteralOrConstant(ScalarExpression? expr)
        {
            if (expr == null) return false;
            if (expr is Literal) return true;
            if (expr is UnaryExpression unary)
            {
                return IsLiteralOrConstant(unary.Expression);
            }
            return false;
        }

        private static bool TryGetNumericValue(ScalarExpression? expr, out decimal value)
        {
            value = 0;
            if (expr == null) return false;

            if (expr is Literal literal)
            {
                string? valStr = null;
                if (literal is IntegerLiteral i) valStr = i.Value;
                else if (literal is NumericLiteral n) valStr = n.Value;
                else if (literal is RealLiteral r) valStr = r.Value;

                if (string.IsNullOrEmpty(valStr)) return false;

                return decimal.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            }

            if (expr is UnaryExpression unary)
            {
                if (unary.UnaryExpressionType == UnaryExpressionType.Negative)
                {
                    if (TryGetNumericValue(unary.Expression, out decimal innerVal))
                    {
                        value = -innerVal;
                        return true;
                    }
                }
                else if (unary.UnaryExpressionType == UnaryExpressionType.Positive)
                {
                    return TryGetNumericValue(unary.Expression, out value);
                }
            }

            return false;
        }

        private static bool TryEvaluateComparison(ScalarExpression? left, ScalarExpression? right, BooleanComparisonType compType, out bool result)
        {
            result = false;
            if (left == null || right == null) return false;

            if (left is StringLiteral strLeft && right is StringLiteral strRight)
            {
                if (compType == BooleanComparisonType.Equals || 
                    compType == BooleanComparisonType.NotEqualToBrackets || 
                    compType == BooleanComparisonType.NotEqualToExclamation)
                {
                    // 1. If exactly identical (case-sensitive and including trailing spaces), they are equal.
                    if (string.Equals(strLeft.Value, strRight.Value, StringComparison.Ordinal))
                    {
                        result = compType == BooleanComparisonType.Equals;
                        return true;
                    }

                    // 2. If they differ even after trimming trailing spaces and ignoring case, they are definitely different.
                    string trimmedLeft = strLeft.Value.TrimEnd(' ');
                    string trimmedRight = strRight.Value.TrimEnd(' ');
                    if (!string.Equals(trimmedLeft, trimmedRight, StringComparison.OrdinalIgnoreCase))
                    {
                        result = compType != BooleanComparisonType.Equals;
                        return true;
                    }

                    // 3. Otherwise, they differ only by case and/or trailing spaces.
                    // We cannot safely optimize because the behavior is collation-dependent.
                    return false;
                }
                return false; // Safe fallback for other comparisons to avoid collation-dependent ordering mismatch
            }

            if (TryGetNumericValue(left, out decimal valLeft) && TryGetNumericValue(right, out decimal valRight))
            {
                int cmp = valLeft.CompareTo(valRight);
                result = EvaluateCmpResult(cmp, compType);
                return true;
            }

            return false;
        }

        private static bool EvaluateCmpResult(int cmp, BooleanComparisonType compType)
        {
            switch (compType)
            {
                case BooleanComparisonType.Equals:
                    return cmp == 0;
                case BooleanComparisonType.NotEqualToBrackets:
                case BooleanComparisonType.NotEqualToExclamation:
                    return cmp != 0;
                case BooleanComparisonType.GreaterThan:
                    return cmp > 0;
                case BooleanComparisonType.GreaterThanOrEqualTo:
                    return cmp >= 0;
                case BooleanComparisonType.LessThan:
                    return cmp < 0;
                case BooleanComparisonType.LessThanOrEqualTo:
                    return cmp <= 0;
                default:
                    return false;
            }
        }

        private static BooleanComparisonType GetInverseComparisonType(BooleanComparisonType compType)
        {
            switch (compType)
            {
                case BooleanComparisonType.GreaterThan:
                    return BooleanComparisonType.LessThan;
                case BooleanComparisonType.GreaterThanOrEqualTo:
                    return BooleanComparisonType.LessThanOrEqualTo;
                case BooleanComparisonType.LessThan:
                    return BooleanComparisonType.GreaterThan;
                case BooleanComparisonType.LessThanOrEqualTo:
                    return BooleanComparisonType.GreaterThanOrEqualTo;
                default:
                    return compType;
            }
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
