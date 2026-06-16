using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring.Visitors
{
    public class ImplicitConversionVisitor : TSqlFragmentVisitor
    {
        private readonly RefactorContext _context;

        public ImplicitConversionVisitor(RefactorContext context)
        {
            _context = context ?? throw new System.ArgumentNullException(nameof(context));
        }

        private static bool ContainsNonAscii(string s)
        {
            if (s == null) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] > 127) return true;
            }
            return false;
        }

        public override void ExplicitVisit(BooleanComparisonExpression node)
        {
            // Check if left is a column and right is a Unicode string literal
            if (node.FirstExpression is ColumnReferenceExpression && 
                node.SecondExpression is StringLiteral literal && 
                literal.IsNational &&
                !ContainsNonAscii(literal.Value))
            {
                literal.IsNational = false;
                _context.Changed = true;
            }
            
            // Check reversed comparison order
            if (node.SecondExpression is ColumnReferenceExpression && 
                node.FirstExpression is StringLiteral literalRev && 
                literalRev.IsNational &&
                !ContainsNonAscii(literalRev.Value))
            {
                literalRev.IsNational = false;
                _context.Changed = true;
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(InPredicate node)
        {
            if (node.Expression is ColumnReferenceExpression)
            {
                foreach (var expr in node.Values)
                {
                    if (expr is StringLiteral literal && literal.IsNational && !ContainsNonAscii(literal.Value))
                    {
                        literal.IsNational = false;
                        _context.Changed = true;
                    }
                }
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(LikePredicate node)
        {
            if (node.FirstExpression is ColumnReferenceExpression && 
                node.SecondExpression is StringLiteral literal && 
                literal.IsNational &&
                !ContainsNonAscii(literal.Value))
            {
                literal.IsNational = false;
                _context.Changed = true;
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(BooleanTernaryExpression node)
        {
            if (node.TernaryExpressionType == BooleanTernaryExpressionType.Between || 
                node.TernaryExpressionType == BooleanTernaryExpressionType.NotBetween)
            {
                if (node.FirstExpression is ColumnReferenceExpression)
                {
                    if (node.SecondExpression is StringLiteral lower && lower.IsNational && !ContainsNonAscii(lower.Value))
                    {
                        lower.IsNational = false;
                        _context.Changed = true;
                    }
                    if (node.ThirdExpression is StringLiteral upper && upper.IsNational && !ContainsNonAscii(upper.Value))
                    {
                        upper.IsNational = false;
                        _context.Changed = true;
                    }
                }
            }

            base.ExplicitVisit(node);
        }
    }
}

