using System;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring.Rules
{
    public class LeftOrSubstringRefactorRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_102_LEFT_SUBSTRING";
        public string Name => "Left/Substring Function Optimizer";
        public string Description => "Rewrites LEFT(Column, n) = 'xxx' or SUBSTRING(Column, 1, n) = 'xxx' to Column LIKE 'xxx%'.";
        public int Priority => 20;

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
                if (node.ComparisonType == BooleanComparisonType.Equals)
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
                if (expression is BooleanComparisonExpression compExpr && compExpr.ComparisonType == BooleanComparisonType.Equals)
                {
                    if (TryOptimize(compExpr.FirstExpression, compExpr.SecondExpression, out var optimized))
                    {
                        return optimized;
                    }
                    if (TryOptimize(compExpr.SecondExpression, compExpr.FirstExpression, out var optimizedReversed))
                    {
                        return optimizedReversed;
                    }
                }
                return expression;
            }

            private bool TryOptimize(ScalarExpression left, ScalarExpression right, out BooleanExpression optimized)
            {
                optimized = null!;
                if (TryGetLeftOrSubstringPattern(left, out var colRef, out int length))
                {
                    if (right is StringLiteral strLit && strLit.Value.Length == length)
                    {
                        if (!ContainsLikeWildcards(strLit.Value))
                        {
                            optimized = new LikePredicate
                            {
                                FirstExpression = colRef,
                                SecondExpression = new StringLiteral
                                {
                                    Value = strLit.Value + "%",
                                    IsNational = strLit.IsNational
                                },
                                NotDefined = false
                            };
                            _context.Log($"Optimized LEFT/SUBSTRING comparison on column {GetColumnName(colRef!)} to LIKE");
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        private static bool IsOptimizeable(ScalarExpression expr, ScalarExpression comparison)
        {
            if (TryGetLeftOrSubstringPattern(expr, out _, out int length))
            {
                if (comparison is StringLiteral strLit && strLit.Value.Length == length)
                {
                    return !ContainsLikeWildcards(strLit.Value);
                }
            }
            return false;
        }

        private static bool TryGetLeftOrSubstringPattern(ScalarExpression expr, out ColumnReferenceExpression? colRef, out int length)
        {
            colRef = null;
            length = 0;

            if (expr is LeftFunctionCall leftCall)
            {
                if (leftCall.Parameters.Count == 2 &&
                    leftCall.Parameters[0] is ColumnReferenceExpression col &&
                    leftCall.Parameters[1] is IntegerLiteral lenLit &&
                    int.TryParse(lenLit.Value, out length))
                {
                    colRef = col;
                    return true;
                }
            }
            else if (expr is FunctionCall funcCall &&
                     string.Equals(funcCall.FunctionName?.Value, "SUBSTRING", StringComparison.OrdinalIgnoreCase))
            {
                if (funcCall.Parameters.Count == 3 &&
                    funcCall.Parameters[0] is ColumnReferenceExpression col &&
                    funcCall.Parameters[1] is IntegerLiteral startLit &&
                    startLit.Value == "1" &&
                    funcCall.Parameters[2] is IntegerLiteral lenLit &&
                    int.TryParse(lenLit.Value, out length))
                {
                    colRef = col;
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLikeWildcards(string value)
        {
            return value.Contains("%") || value.Contains("_") || value.Contains("[") || value.Contains("]");
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
