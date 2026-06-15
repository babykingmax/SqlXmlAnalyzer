using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public class NonSargableExpressionInfo
    {
        public string ExpressionText { get; set; } = "";
        public string IssueType { get; set; } = ""; // "Arithmetic", "Function", "ColumnComparison"
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public int RiskScore { get; set; }
        public string Description { get; set; } = "";
    }

    public class NonSargableDetector
    {
        public static List<NonSargableExpressionInfo> Detect(string sql)
        {
            var results = new List<NonSargableExpressionInfo>();
            if (string.IsNullOrWhiteSpace(sql)) return results;

            var parser = new TSql160Parser(true);
            using (var reader = new StringReader(sql))
            {
                var fragment = parser.Parse(reader, out var errors);
                if (errors.Count > 0)
                {
                    return results;
                }
                results.AddRange(Detect(fragment));
            }
            return results;
        }

        public static List<NonSargableExpressionInfo> Detect(TSqlFragment fragment)
        {
            var visitor = new NonSargableDetectorVisitor();
            fragment.Accept(visitor);
            return visitor.GetNonSargableExpressions();
        }
    }

    public class NonSargableDetectorVisitor : TSqlFragmentVisitor
    {
        private readonly List<NonSargableExpressionInfo> _findings = new();

        public List<NonSargableExpressionInfo> GetNonSargableExpressions() => _findings;

        private string GetNodeText(TSqlFragment node)
        {
            var generator = new Sql160ScriptGenerator();
            generator.GenerateScript(node, out string text);
            return text?.Trim() ?? "";
        }

        private static bool ContainsColumnReference(TSqlFragment node, out string columnName)
        {
            columnName = "";
            var finder = new ColumnFinder();
            node.Accept(finder);
            if (finder.Columns.Count > 0)
            {
                var col = finder.Columns[0];
                var mpi = col.MultiPartIdentifier;
                if (mpi != null && mpi.Identifiers.Count > 0)
                {
                    columnName = mpi.Identifiers[mpi.Identifiers.Count - 1].Value;
                }
                return true;
            }
            return false;
        }

        private class ColumnFinder : TSqlFragmentVisitor
        {
            public List<ColumnReferenceExpression> Columns { get; } = new();
            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                Columns.Add(node);
                base.ExplicitVisit(node);
            }
        }

        // Check if an expression is non-sargable
        private void CheckExpression(ScalarExpression expr, BooleanComparisonType? compType = null)
        {
            if (expr == null) return;

            // 1. Check if it is a function call wrapping a column
            if (IsFunctionOnColumn(expr, out var funcName, out var columnName))
            {
                bool isRefactorable = false;

                // Let's check refactorability
                if (string.Equals(funcName, "LEFT", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(funcName, "SUBSTRING", StringComparison.OrdinalIgnoreCase))
                {
                    if (compType == BooleanComparisonType.Equals || 
                        compType == BooleanComparisonType.NotEqualToBrackets || 
                        compType == BooleanComparisonType.NotEqualToExclamation)
                    {
                        isRefactorable = true;
                    }
                }
                else if (string.Equals(funcName, "ISNULL", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(funcName, "COALESCE", StringComparison.OrdinalIgnoreCase))
                {
                    isRefactorable = true;
                }
                else if (expr is ConvertCall convert)
                {
                    if (convert.DataType is SqlDataTypeReference sqlDataRef &&
                        sqlDataRef.SqlDataTypeOption == SqlDataTypeOption.Date)
                    {
                        isRefactorable = true;
                    }
                }
                else if (expr is CastCall cast)
                {
                    if (cast.DataType is SqlDataTypeReference sqlDataRef &&
                        sqlDataRef.SqlDataTypeOption == SqlDataTypeOption.Date)
                    {
                        isRefactorable = true;
                    }
                }

                if (!isRefactorable)
                {
                    string exprText = GetNodeText(expr);
                    _findings.Add(new NonSargableExpressionInfo
                    {
                        ExpressionText = exprText,
                        IssueType = "Function",
                        ColumnName = columnName,
                        RiskScore = 80,
                        Description = $"对列 [{columnName}] 使用了无法自动改写的非 SARGable 函数 [{funcName}] ({exprText})。这会导致索引失效并触发全表扫描。"
                    });
                }
                return;
            }

            // 2. Check if it is arithmetic on a column
            if (IsArithmeticOnColumn(expr, out columnName))
            {
                string exprText = GetNodeText(expr);
                _findings.Add(new NonSargableExpressionInfo
                {
                    ExpressionText = exprText,
                    IssueType = "Arithmetic",
                    ColumnName = columnName,
                    RiskScore = 70,
                    Description = $"在过滤条件中对列 [{columnName}] 进行了算术运算 ({exprText})。这会导致 SQL Server 优化器无法进行索引 Seek。建议将计算移到等式右侧。"
                });
                return;
            }
        }

        private bool IsFunctionOnColumn(ScalarExpression expr, out string funcName, out string columnName)
        {
            funcName = "";
            columnName = "";

            if (expr is FunctionCall func)
            {
                funcName = func.FunctionName?.Value ?? "";
                if (ContainsColumnReference(func, out columnName)) return true;
            }
            else if (expr is LeftFunctionCall leftFunc)
            {
                funcName = "LEFT";
                if (ContainsColumnReference(leftFunc, out columnName)) return true;
            }
            else if (expr is ConvertCall convert)
            {
                funcName = "CONVERT";
                if (ContainsColumnReference(convert, out columnName)) return true;
            }
            else if (expr is CastCall cast)
            {
                funcName = "CAST";
                if (ContainsColumnReference(cast, out columnName)) return true;
            }

            return false;
        }

        private bool IsArithmeticOnColumn(ScalarExpression expr, out string columnName)
        {
            columnName = "";
            if (expr is BinaryExpression binary)
            {
                if (binary.BinaryExpressionType == BinaryExpressionType.Add ||
                    binary.BinaryExpressionType == BinaryExpressionType.Subtract ||
                    binary.BinaryExpressionType == BinaryExpressionType.Multiply ||
                    binary.BinaryExpressionType == BinaryExpressionType.Divide ||
                    binary.BinaryExpressionType == BinaryExpressionType.Modulo ||
                    binary.BinaryExpressionType == BinaryExpressionType.BitwiseAnd ||
                    binary.BinaryExpressionType == BinaryExpressionType.BitwiseOr ||
                    binary.BinaryExpressionType == BinaryExpressionType.BitwiseXor)
                {
                    if (ContainsColumnReference(binary, out columnName)) return true;
                }
            }
            else if (expr is UnaryExpression unary)
            {
                if (unary.UnaryExpressionType == UnaryExpressionType.Positive ||
                    unary.UnaryExpressionType == UnaryExpressionType.Negative ||
                    unary.UnaryExpressionType == UnaryExpressionType.BitwiseNot)
                {
                    if (ContainsColumnReference(unary, out columnName)) return true;
                }
            }

            return false;
        }

        public override void ExplicitVisit(BooleanComparisonExpression node)
        {
            // First check for column-to-column inequality
            string leftColName = "";
            string rightColName = "";
            bool leftHasCol = ContainsColumnReference(node.FirstExpression, out leftColName);
            bool rightHasCol = ContainsColumnReference(node.SecondExpression, out rightColName);

            if (leftHasCol && rightHasCol && node.ComparisonType != BooleanComparisonType.Equals)
            {
                string exprText = GetNodeText(node);
                _findings.Add(new NonSargableExpressionInfo
                {
                    ExpressionText = exprText,
                    IssueType = "ColumnComparison",
                    ColumnName = $"{leftColName}, {rightColName}",
                    RiskScore = 50,
                    Description = $"在过滤条件中存在列与列的非等值比较 ({exprText})，此类谓词无法直接作为索引 Seek 过滤条件。"
                });
            }
            else
            {
                CheckExpression(node.FirstExpression, node.ComparisonType);
                CheckExpression(node.SecondExpression, node.ComparisonType);
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(LikePredicate node)
        {
            CheckExpression(node.FirstExpression);
            CheckExpression(node.SecondExpression);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(InPredicate node)
        {
            CheckExpression(node.Expression);
            foreach (var val in node.Values)
            {
                CheckExpression(val);
            }
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(BooleanTernaryExpression node)
        {
            if (node.TernaryExpressionType == BooleanTernaryExpressionType.Between || 
                node.TernaryExpressionType == BooleanTernaryExpressionType.NotBetween)
            {
                CheckExpression(node.FirstExpression);
                CheckExpression(node.SecondExpression);
                CheckExpression(node.ThirdExpression);
            }
            base.ExplicitVisit(node);
        }
    }
}
