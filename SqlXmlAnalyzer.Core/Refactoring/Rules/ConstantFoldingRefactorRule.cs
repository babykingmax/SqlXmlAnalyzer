using System;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring.Rules
{
    public class ConstantFoldingRefactorRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_104_CONST_FOLD";
        public string Name => "Constant Folding Optimizer";
        public string Description => "Folds simple arithmetic on column references inside comparison expressions (e.g. Col + 10 > 50 to Col > 40).";
        public int Priority => 40;

        public bool CanApply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new FinderVisitor();
            fragment.Accept(visitor);
            return visitor.Found;
        }

        public TSqlFragment Apply(TSqlFragment fragment, RefactorContext context)
        {
            if (fragment is BooleanExpression boolExpr)
            {
                var visitor = new RewriteVisitor(context);
                var replaced = visitor.Rewrite(boolExpr);
                if (replaced != boolExpr) return replaced;
            }
            else
            {
                var visitor = new RewriteVisitor(context);
                fragment.Accept(visitor);
            }
            return fragment;
        }

        private class FinderVisitor : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                if (TryFoldArithmetic(node.FirstExpression, node.SecondExpression, out _, out _) ||
                    TryFoldArithmetic(node.SecondExpression, node.FirstExpression, out _, out _))
                {
                    Found = true;
                    return;
                }
                base.ExplicitVisit(node);
            }
        }

        private class RewriteVisitor : BooleanExpressionReplacementVisitor
        {
            public RewriteVisitor(RefactorContext context) : base(context)
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
                    if (TryFoldArithmetic(compExpr.FirstExpression, compExpr.SecondExpression, out var colRef, out var foldedVal))
                    {
                        _context.Log($"Folded constant arithmetic on column {GetColumnName(colRef!)}");
                        return new BooleanComparisonExpression
                        {
                            ComparisonType = compExpr.ComparisonType,
                            FirstExpression = colRef,
                            SecondExpression = foldedVal
                        };
                    }
                    if (TryFoldArithmetic(compExpr.SecondExpression, compExpr.FirstExpression, out var colRefRev, out var foldedValRev))
                    {
                        _context.Log($"Folded constant arithmetic on column {GetColumnName(colRefRev!)}");
                        return new BooleanComparisonExpression
                        {
                            ComparisonType = compExpr.ComparisonType,
                            FirstExpression = foldedValRev,
                            SecondExpression = colRefRev
                        };
                    }
                }
                return expression;
            }
        }

        private static bool TryFoldArithmetic(
            ScalarExpression expr, 
            ScalarExpression comparisonVal, 
            out ColumnReferenceExpression? colRef, 
            out ScalarExpression? newComparisonVal)
        {
            colRef = null;
            newComparisonVal = null;

            if (TryGetIntegerValue(comparisonVal, out long destVal))
            {
                if (expr is BinaryExpression binExpr)
                {
                    try
                    {
                        if (binExpr.BinaryExpressionType == BinaryExpressionType.Add)
                        {
                            if (binExpr.FirstExpression is ColumnReferenceExpression col &&
                                TryGetIntegerValue(binExpr.SecondExpression, out long offset))
                            {
                                colRef = col;
                                checked
                                {
                                    newComparisonVal = CreateIntegerLiteralExpression(destVal - offset);
                                }
                                return true;
                            }
                            else if (binExpr.SecondExpression is ColumnReferenceExpression col2 &&
                                     TryGetIntegerValue(binExpr.FirstExpression, out long offset2))
                            {
                                colRef = col2;
                                checked
                                {
                                    newComparisonVal = CreateIntegerLiteralExpression(destVal - offset2);
                                }
                                return true;
                            }
                        }
                        else if (binExpr.BinaryExpressionType == BinaryExpressionType.Subtract)
                        {
                            if (binExpr.FirstExpression is ColumnReferenceExpression col &&
                                TryGetIntegerValue(binExpr.SecondExpression, out long offset))
                            {
                                colRef = col;
                                checked
                                {
                                    newComparisonVal = CreateIntegerLiteralExpression(destVal + offset);
                                }
                                return true;
                            }
                        }
                    }
                    catch (OverflowException)
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static ScalarExpression CreateIntegerLiteralExpression(long value)
        {
            if (value < 0)
            {
                var unary = new UnaryExpression();
                unary.UnaryExpressionType = UnaryExpressionType.Negative;
                string valStr = value == long.MinValue ? "9223372036854775808" : (-value).ToString();
                unary.Expression = new IntegerLiteral { Value = valStr };
                return unary;
            }
            else
            {
                return new IntegerLiteral { Value = value.ToString() };
            }
        }

        private static bool TryGetIntegerValue(ScalarExpression? expr, out long value)
        {
            value = 0;
            if (expr == null) return false;

            if (expr is IntegerLiteral literal)
            {
                return long.TryParse(literal.Value, out value);
            }

            if (expr is UnaryExpression unary)
            {
                if (unary.UnaryExpressionType == UnaryExpressionType.Negative)
                {
                    if (TryGetIntegerValue(unary.Expression, out long innerVal))
                    {
                        checked
                        {
                            value = -innerVal;
                        }
                        return true;
                    }
                }
                else if (unary.UnaryExpressionType == UnaryExpressionType.Positive)
                {
                    return TryGetIntegerValue(unary.Expression, out value);
                }
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
