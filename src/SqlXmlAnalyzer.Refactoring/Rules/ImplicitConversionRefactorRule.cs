using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;

namespace SqlXmlAnalyzer.Refactoring.Rules
{
    public class ImplicitConversionRefactorRule : ISqlRefactorRule
    {
        public string RuleId => "REF_RULE_105_IMPLICIT_CONV";
        public string Name => "Implicit Conversion Optimizer";
        public string Description => "Removes redundant Unicode N prefix from string literals compared to columns when there are no non-ASCII characters to prevent index scans.";
        public int Priority => 50;

        public bool CanApply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new FinderVisitor(context);
            fragment.Accept(visitor);
            return visitor.Found;
        }

        public RuleResult Apply(TSqlFragment fragment, RefactorContext context)
        {
            var visitor = new RewriteVisitor(context);
            fragment.Accept(visitor);
            if (visitor.Changed)
            {
                var colsStr = visitor.OptimizedColumns.Count > 0
                    ? " on column(s) " + string.Join(", ", visitor.OptimizedColumns.Distinct())
                    : "";
                var desc = $"Removed redundant Unicode N prefix from string literal(s){colsStr}";
                return new RuleResult(fragment, true, desc);
            }
            return new RuleResult(fragment, false, null);
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

        private static string GetColumnName(ColumnReferenceExpression colRef)
        {
            var mpi = colRef.MultiPartIdentifier;
            if (mpi != null && mpi.Identifiers.Count > 0)
            {
                return mpi.Identifiers[mpi.Identifiers.Count - 1].Value;
            }
            return "Column";
        }

        private static bool IsColumnAffected(string columnName, RefactorContext context)
        {
            // If the analysis report is empty (no plan analyzed), we allow it by default (heuristic fallback)
            if (context.Analysis == null || context.Analysis.Issues == null || context.Analysis.Issues.Count == 0)
            {
                return true;
            }

            foreach (var issue in context.Analysis.Issues)
            {
                if (issue.IssueType == "RULE_001_IMPLICIT_CONV")
                {
                    if (issue.ColumnName != null && string.Equals(issue.ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (issue.Description != null)
                    {
                        // Search for column name in various common formats
                        if (issue.Description.Contains($"[{columnName}]", StringComparison.OrdinalIgnoreCase) ||
                            issue.Description.Contains($".{columnName}", StringComparison.OrdinalIgnoreCase) ||
                            issue.Description.Contains($" {columnName} ", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private class FinderVisitor : TSqlFragmentVisitor
        {
            private readonly RefactorContext _context;
            public bool Found { get; private set; }

            public FinderVisitor(RefactorContext context)
            {
                _context = context;
            }

            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                if (Found) return;

                if (node.FirstExpression is ColumnReferenceExpression colRef &&
                    node.SecondExpression is StringLiteral literal &&
                    literal.IsNational &&
                    !ContainsNonAscii(literal.Value) &&
                    IsColumnAffected(GetColumnName(colRef), _context))
                {
                    Found = true;
                    return;
                }

                if (node.SecondExpression is ColumnReferenceExpression colRefRev &&
                    node.FirstExpression is StringLiteral literalRev &&
                    literalRev.IsNational &&
                    !ContainsNonAscii(literalRev.Value) &&
                    IsColumnAffected(GetColumnName(colRefRev), _context))
                {
                    Found = true;
                    return;
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(InPredicate node)
            {
                if (Found) return;

                if (node.Expression is ColumnReferenceExpression colRef &&
                    IsColumnAffected(GetColumnName(colRef), _context))
                {
                    foreach (var expr in node.Values)
                    {
                        if (expr is StringLiteral literal && literal.IsNational && !ContainsNonAscii(literal.Value))
                        {
                            Found = true;
                            return;
                        }
                    }
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(LikePredicate node)
            {
                if (Found) return;

                if (node.FirstExpression is ColumnReferenceExpression colRef &&
                    node.SecondExpression is StringLiteral literal &&
                    literal.IsNational &&
                    !ContainsNonAscii(literal.Value) &&
                    IsColumnAffected(GetColumnName(colRef), _context))
                {
                    Found = true;
                    return;
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(BooleanTernaryExpression node)
            {
                if (Found) return;

                if ((node.TernaryExpressionType == BooleanTernaryExpressionType.Between ||
                     node.TernaryExpressionType == BooleanTernaryExpressionType.NotBetween) &&
                    node.FirstExpression is ColumnReferenceExpression colRef &&
                    IsColumnAffected(GetColumnName(colRef), _context))
                {
                    if (node.SecondExpression is StringLiteral lower && lower.IsNational && !ContainsNonAscii(lower.Value))
                    {
                        Found = true;
                        return;
                    }
                    if (node.ThirdExpression is StringLiteral upper && upper.IsNational && !ContainsNonAscii(upper.Value))
                    {
                        Found = true;
                        return;
                    }
                }

                base.ExplicitVisit(node);
            }
        }

        private class RewriteVisitor : TSqlFragmentVisitor
        {
            private readonly RefactorContext _context;
            public bool Changed { get; private set; }
            public List<string> OptimizedColumns { get; } = new();

            public RewriteVisitor(RefactorContext context)
            {
                _context = context;
            }

            public override void ExplicitVisit(BooleanComparisonExpression node)
            {
                if (node.FirstExpression is ColumnReferenceExpression colRef &&
                    node.SecondExpression is StringLiteral literal &&
                    literal.IsNational &&
                    !ContainsNonAscii(literal.Value))
                {
                    var colName = GetColumnName(colRef);
                    if (IsColumnAffected(colName, _context))
                    {
                        literal.IsNational = false;
                        Changed = true;
                        OptimizedColumns.Add(colName);
                        _context.Log($"Removed redundant Unicode N prefix from literal compared to column {colName}");
                    }
                }

                if (node.SecondExpression is ColumnReferenceExpression colRefRev &&
                    node.FirstExpression is StringLiteral literalRev &&
                    literalRev.IsNational &&
                    !ContainsNonAscii(literalRev.Value))
                {
                    var colName = GetColumnName(colRefRev);
                    if (IsColumnAffected(colName, _context))
                    {
                        literalRev.IsNational = false;
                        Changed = true;
                        OptimizedColumns.Add(colName);
                        _context.Log($"Removed redundant Unicode N prefix from literal compared to column {colName}");
                    }
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(InPredicate node)
            {
                if (node.Expression is ColumnReferenceExpression colRef)
                {
                    var colName = GetColumnName(colRef);
                    if (IsColumnAffected(colName, _context))
                    {
                        foreach (var expr in node.Values)
                        {
                            if (expr is StringLiteral literal && literal.IsNational && !ContainsNonAscii(literal.Value))
                            {
                                literal.IsNational = false;
                                Changed = true;
                                OptimizedColumns.Add(colName);
                                _context.Log($"Removed redundant Unicode N prefix from IN literal compared to column {colName}");
                            }
                        }
                    }
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(LikePredicate node)
            {
                if (node.FirstExpression is ColumnReferenceExpression colRef &&
                    node.SecondExpression is StringLiteral literal &&
                    literal.IsNational &&
                    !ContainsNonAscii(literal.Value))
                {
                    var colName = GetColumnName(colRef);
                    if (IsColumnAffected(colName, _context))
                    {
                        literal.IsNational = false;
                        Changed = true;
                        OptimizedColumns.Add(colName);
                        _context.Log($"Removed redundant Unicode N prefix from LIKE literal compared to column {colName}");
                    }
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(BooleanTernaryExpression node)
            {
                if ((node.TernaryExpressionType == BooleanTernaryExpressionType.Between ||
                     node.TernaryExpressionType == BooleanTernaryExpressionType.NotBetween) &&
                    node.FirstExpression is ColumnReferenceExpression colRef)
                {
                    var colName = GetColumnName(colRef);
                    if (IsColumnAffected(colName, _context))
                    {
                        if (node.SecondExpression is StringLiteral lower && lower.IsNational && !ContainsNonAscii(lower.Value))
                        {
                            lower.IsNational = false;
                            Changed = true;
                            OptimizedColumns.Add(colName);
                            _context.Log($"Removed redundant Unicode N prefix from BETWEEN lower literal compared to column {colName}");
                        }
                        if (node.ThirdExpression is StringLiteral upper && upper.IsNational && !ContainsNonAscii(upper.Value))
                        {
                            upper.IsNational = false;
                            Changed = true;
                            OptimizedColumns.Add(colName);
                            _context.Log($"Removed redundant Unicode N prefix from BETWEEN upper literal compared to column {colName}");
                        }
                    }
                }

                base.ExplicitVisit(node);
            }
        }
    }
}
