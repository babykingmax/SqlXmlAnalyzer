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
            private readonly RefactorContext _context;

            public RewriteVisitor(RefactorContext context)
            {
                _context = context;
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
            out IntegerLiteral? newComparisonVal)
        {
            colRef = null;
            newComparisonVal = null;

            if (comparisonVal is IntegerLiteral destLit && long.TryParse(destLit.Value, out long destVal))
            {
                if (expr is BinaryExpression binExpr)
                {
                    if (binExpr.BinaryExpressionType == BinaryExpressionType.Add)
                    {
                        if (binExpr.FirstExpression is ColumnReferenceExpression col &&
                            binExpr.SecondExpression is IntegerLiteral lit &&
                            long.TryParse(lit.Value, out long offset))
                        {
                            colRef = col;
                            newComparisonVal = new IntegerLiteral { Value = (destVal - offset).ToString() };
                            return true;
                        }
                        else if (binExpr.SecondExpression is ColumnReferenceExpression col2 &&
                                 binExpr.FirstExpression is IntegerLiteral lit2 &&
                                 long.TryParse(lit2.Value, out long offset2))
                        {
                            colRef = col2;
                            newComparisonVal = new IntegerLiteral { Value = (destVal - offset2).ToString() };
                            return true;
                        }
                    }
                    else if (binExpr.BinaryExpressionType == BinaryExpressionType.Subtract)
                    {
                        if (binExpr.FirstExpression is ColumnReferenceExpression col &&
                            binExpr.SecondExpression is IntegerLiteral lit &&
                            long.TryParse(lit.Value, out long offset))
                        {
                            colRef = col;
                            newComparisonVal = new IntegerLiteral { Value = (destVal + offset).ToString() };
                            return true;
                        }
                    }
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
