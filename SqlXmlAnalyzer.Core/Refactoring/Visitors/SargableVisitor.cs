using System;
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring.Visitors
{
    public class SargableVisitor : TSqlFragmentVisitor
    {
        private readonly SqlXmlAnalyzer.Core.RefactorContext _context;

        public SargableVisitor(SqlXmlAnalyzer.Core.RefactorContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache =
            new ConcurrentDictionary<Type, PropertyInfo[]>();

        private PropertyInfo[] GetBooleanExpressionProperties(Type type)
        {
            return _propertyCache.GetOrAdd(type, t =>
            {
                var list = new System.Collections.Generic.List<PropertyInfo>();
                foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (typeof(BooleanExpression).IsAssignableFrom(prop.PropertyType) && prop.CanRead && prop.CanWrite)
                    {
                        list.Add(prop);
                    }
                }
                return list.ToArray();
            });
        }

        public override void Visit(TSqlFragment node)
        {
            if (node == null) return;

            // Only inspect properties of nodes that are NOT BooleanExpressions themselves.
            // This avoids redundant traversals since OptimizeBooleanExpression processes nested expressions recursively.
            if (!(node is BooleanExpression))
            {
                var props = GetBooleanExpressionProperties(node.GetType());
                foreach (var prop in props)
                {
                    var val = prop.GetValue(node) as BooleanExpression;
                    if (val != null)
                    {
                        var optimized = OptimizeBooleanExpression(val);
                        if (optimized != val)
                        {
                            prop.SetValue(node, optimized);
                            _context.Changed = true;
                        }
                    }
                }
            }

            base.Visit(node);
        }

        private BooleanExpression? OptimizeBooleanExpression(BooleanExpression? expr)
        {
            if (expr == null) return null;

            // 1. Recursive optimization for nested boolean structures
            if (expr is BooleanBinaryExpression binary)
            {
                binary.FirstExpression = OptimizeBooleanExpression(binary.FirstExpression);
                binary.SecondExpression = OptimizeBooleanExpression(binary.SecondExpression);

                if (binary.BinaryExpressionType == BooleanBinaryExpressionType.And)
                {
                    var list = new System.Collections.Generic.List<BooleanExpression>();
                    FlattenAnd(binary, list);
                    var filtered = new System.Collections.Generic.List<BooleanExpression>();
                    foreach (var item in list)
                    {
                        if (TryEvaluateStaticBoolean(item, out bool val))
                        {
                            if (!val)
                            {
                                return item; // One false makes the whole AND false
                            }
                        }
                        else
                        {
                            filtered.Add(item);
                        }
                    }

                    if (filtered.Count == 0)
                    {
                        return list[0];
                    }

                    var unique = DeduplicateExpressions(filtered);
                    return ReconstructAnd(unique);
                }
                else if (binary.BinaryExpressionType == BooleanBinaryExpressionType.Or)
                {
                    var list = new System.Collections.Generic.List<BooleanExpression>();
                    FlattenOr(binary, list);
                    var filtered = new System.Collections.Generic.List<BooleanExpression>();
                    foreach (var item in list)
                    {
                        if (TryEvaluateStaticBoolean(item, out bool val))
                        {
                            if (val)
                            {
                                return item; // One true makes the whole OR true
                            }
                        }
                        else
                        {
                            filtered.Add(item);
                        }
                    }

                    if (filtered.Count == 0)
                    {
                        return list[0];
                    }

                    var unique = DeduplicateExpressions(filtered);
                    return ReconstructOr(unique);
                }

                return binary;
            }

            if (expr is BooleanParenthesisExpression paren)
            {
                paren.Expression = OptimizeBooleanExpression(paren.Expression);
                return paren;
            }

            if (expr is BooleanNotExpression notExpr)
            {
                notExpr.Expression = OptimizeBooleanExpression(notExpr.Expression);
                return notExpr;
            }

            if (expr is InPredicate inPred)
            {
                return OptimizeInPredicate(inPred);
            }

            // 2. Leaf node optimizations (BooleanComparisonExpression)
            if (expr is BooleanComparisonExpression comparison)
            {
                return OptimizeComparison(comparison);
            }

            return expr;
        }

        private BooleanExpression? OptimizeComparison(BooleanComparisonExpression? node)
        {
            if (node == null) return null;

            // Apply left-right normalization: column/function always on the left, constant/literal on the right
            if (IsLiteralOrConstant(node.FirstExpression) && !IsLiteralOrConstant(node.SecondExpression))
            {
                var temp = node.FirstExpression;
                node.FirstExpression = node.SecondExpression;
                node.SecondExpression = temp;
                node.ComparisonType = GetInverseComparisonType(node.ComparisonType);
            }

            // A. Check LEFT or SUBSTRING to LIKE optimization (Equals and NotEqualTo only)
            if (node.ComparisonType == BooleanComparisonType.Equals ||
                node.ComparisonType == BooleanComparisonType.NotEqualToBrackets ||
                node.ComparisonType == BooleanComparisonType.NotEqualToExclamation)
            {
                if (TryOptimizeLeftOrSubstring(node.FirstExpression, node.SecondExpression, node.ComparisonType, out var optimizedLeft))
                {
                    return optimizedLeft;
                }
            }

            // B. Check YEAR(Col) optimization (supports =, <>, !=, >, >=, <, <=)
            if (TryOptimizeYear(node.FirstExpression, node.SecondExpression, node.ComparisonType, out var optimizedYear))
            {
                return optimizedYear;
            }

            // C. Check CONVERT(date, Col) or CAST(Col AS date) optimization (supports =, <>, !=, >, >=, <, <=)
            if (TryOptimizeConvertOrCastDate(node.FirstExpression, node.SecondExpression, node.ComparisonType, out var optimizedConvert))
            {
                return optimizedConvert;
            }

            // D. Check ISNULL or COALESCE optimizations
            if (TryOptimizeIsNull(node.FirstExpression, node.SecondExpression, node.ComparisonType, out var optimizedIsNull))
            {
                return optimizedIsNull;
            }

            // E. Check DATEADD optimizations
            if (TryOptimizeDateAdd(node.FirstExpression, node.SecondExpression, node.ComparisonType, out var optimizedDateAdd))
            {
                return optimizedDateAdd;
            }

            // F. Check DATEDIFF optimizations
            if (TryOptimizeDateDiff(node.FirstExpression, node.SecondExpression, node.ComparisonType, out var optimizedDateDiff))
            {
                return optimizedDateDiff;
            }

            // G. Check ABS optimizations
            if (TryOptimizeAbs(node.FirstExpression, node.SecondExpression, node.ComparisonType, out var optimizedAbs))
            {
                return optimizedAbs;
            }

            // H. Check RTRIM optimizations
            if (TryOptimizeRTrim(node.FirstExpression, node.SecondExpression, node.ComparisonType, out var optimizedRTrim))
            {
                return optimizedRTrim;
            }

            return node;
        }

        private BooleanExpression OptimizeInPredicate(InPredicate inPred)
        {
            if (TryGetIsNullFunctionPattern(inPred.Expression, out var colRef, out var defaultValue))
            {
                if (defaultValue != null && inPred.Values != null && inPred.Values.Count > 0)
                {
                    if (!IsLiteralOrConstant(defaultValue))
                    {
                        return inPred;
                    }

                    foreach (var val in inPred.Values)
                    {
                        if (!IsLiteralOrConstant(val))
                        {
                            return inPred;
                        }

                        if (defaultValue is StringLiteral strDefault && val is StringLiteral strVal)
                        {
                            if (!string.Equals(strDefault.Value, strVal.Value, StringComparison.Ordinal))
                            {
                                string t1 = strDefault.Value.TrimEnd(' ');
                                string t2 = strVal.Value.TrimEnd(' ');
                                if (string.Equals(t1, t2, StringComparison.OrdinalIgnoreCase))
                                {
                                    return inPred;
                                }
                            }
                        }
                    }

                    bool hasDefault = false;
                    foreach (var val in inPred.Values)
                    {
                        if (AreValuesEqual(defaultValue, val))
                        {
                            hasDefault = true;
                            break;
                        }
                    }

                    var newInPred = new InPredicate
                    {
                        Expression = CloneColumnReference(colRef),
                        NotDefined = inPred.NotDefined
                    };
                    foreach (var val in inPred.Values)
                    {
                        newInPred.Values.Add(val);
                    }

                    bool isNotIn = inPred.NotDefined;

                    if (isNotIn == hasDefault)
                    {
                        return new BooleanBinaryExpression
                        {
                            BinaryExpressionType = BooleanBinaryExpressionType.And,
                            FirstExpression = newInPred,
                            SecondExpression = new BooleanIsNullExpression
                            {
                                Expression = CloneColumnReference(colRef),
                                IsNot = true
                            }
                        };
                    }
                    else
                    {
                        return new BooleanParenthesisExpression
                        {
                            Expression = new BooleanBinaryExpression
                            {
                                BinaryExpressionType = BooleanBinaryExpressionType.Or,
                                FirstExpression = newInPred,
                                SecondExpression = new BooleanIsNullExpression
                                {
                                    Expression = CloneColumnReference(colRef),
                                    IsNot = false
                                }
                            }
                        };
                    }
                }
            }

            return inPred;
        }

        private bool TryOptimizeLeftOrSubstring(
            ScalarExpression? potentialFunc,
            ScalarExpression? comparisonExpr,
            BooleanComparisonType comparisonType,
            out BooleanExpression? optimized)
        {
            optimized = null;

            if (comparisonExpr is StringLiteral stringLiteral)
            {
                if (TryGetLeftOrSubstringPattern(potentialFunc, out var columnRef, out int length))
                {
                    // Correctness check: the literal string length must match the function length parameter
                    if (stringLiteral.Value != null && stringLiteral.Value.Length == length)
                    {
                        if (stringLiteral.Value.EndsWith(" ") || stringLiteral.Value.EndsWith("\t"))
                        {
                            return false;
                        }

                        string escaped = EscapeLikeWildcards(stringLiteral.Value);
                        string pattern = escaped + "%";

                        var patternLiteral = new StringLiteral
                        {
                            Value = pattern,
                            IsNational = stringLiteral.IsNational
                        };

                        optimized = new LikePredicate
                        {
                            FirstExpression = columnRef,
                            SecondExpression = patternLiteral,
                            NotDefined = (comparisonType == BooleanComparisonType.NotEqualToBrackets ||
                                          comparisonType == BooleanComparisonType.NotEqualToExclamation)
                        };
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetLeftOrSubstringPattern(ScalarExpression? expr, out ColumnReferenceExpression? columnRef, out int length)
        {
            columnRef = null;
            length = 0;

            if (expr is LeftFunctionCall leftCall)
            {
                if (leftCall.Parameters != null &&
                    leftCall.Parameters.Count == 2 &&
                    leftCall.Parameters[0] is ColumnReferenceExpression col &&
                    leftCall.Parameters[1] is IntegerLiteral lenLiteral)
                {
                    if (int.TryParse(lenLiteral.Value, out int len) && len > 0)
                    {
                        columnRef = col;
                        length = len;
                        return true;
                    }
                }
            }
            else if (expr is FunctionCall funcCall && funcCall.FunctionName != null)
            {
                string funcName = funcCall.FunctionName.Value;
                if (string.Equals(funcName, "LEFT", StringComparison.OrdinalIgnoreCase))
                {
                    if (funcCall.Parameters != null &&
                        funcCall.Parameters.Count == 2 &&
                        funcCall.Parameters[0] is ColumnReferenceExpression col &&
                        funcCall.Parameters[1] is IntegerLiteral lenLiteral)
                    {
                        if (int.TryParse(lenLiteral.Value, out int len) && len > 0)
                        {
                            columnRef = col;
                            length = len;
                            return true;
                        }
                    }
                }
                else if (string.Equals(funcName, "SUBSTRING", StringComparison.OrdinalIgnoreCase))
                {
                    if (funcCall.Parameters != null &&
                        funcCall.Parameters.Count == 3 &&
                        funcCall.Parameters[0] is ColumnReferenceExpression col &&
                        funcCall.Parameters[1] is IntegerLiteral startLiteral &&
                        funcCall.Parameters[2] is IntegerLiteral lenLiteral)
                    {
                        if (startLiteral.Value == "1" &&
                            int.TryParse(lenLiteral.Value, out int len) && len > 0)
                        {
                            columnRef = col;
                            length = len;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TryGetIsNullFunctionPattern(ScalarExpression? expr, out ColumnReferenceExpression? colRef, out ScalarExpression? defaultValue)
        {
            colRef = null;
            defaultValue = null;

            if (expr is FunctionCall funcCall && funcCall.FunctionName != null)
            {
                string funcName = funcCall.FunctionName.Value;
                if (string.Equals(funcName, "ISNULL", StringComparison.OrdinalIgnoreCase))
                {
                    if (funcCall.Parameters != null &&
                        funcCall.Parameters.Count == 2 &&
                        funcCall.Parameters[0] is ColumnReferenceExpression col)
                    {
                        colRef = col;
                        defaultValue = funcCall.Parameters[1];
                        return true;
                    }
                }
            }
            else if (expr is CoalesceExpression coalesce)
            {
                if (coalesce.Expressions != null &&
                    coalesce.Expressions.Count == 2 &&
                    coalesce.Expressions[0] is ColumnReferenceExpression col)
                {
                    colRef = col;
                    defaultValue = coalesce.Expressions[1];
                    return true;
                }
            }
            return false;
        }

        private bool AreValuesEqual(ScalarExpression? a, ScalarExpression? b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (a is StringLiteral strA && b is StringLiteral strB)
            {
                return string.Equals(strA.Value, strB.Value, StringComparison.Ordinal);
            }

            if (TryGetNumericValue(a, out decimal valA) && TryGetNumericValue(b, out decimal valB))
            {
                return valA == valB;
            }

            return false;
        }

        private bool IsLiteralOrConstant(ScalarExpression? expr)
        {
            if (expr == null) return false;
            if (expr is Literal) return true;
            if (expr is UnaryExpression unary)
            {
                return IsLiteralOrConstant(unary.Expression);
            }
            return false;
        }

        private bool TryGetNumericValue(ScalarExpression? expr, out decimal value)
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

                return decimal.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
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

        private string EscapeLikeWildcards(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new System.Text.StringBuilder();
            foreach (char c in input)
            {
                if (c == '[') sb.Append("[[]");
                else if (c == '%') sb.Append("[%]");
                else if (c == '_') sb.Append("[_]");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private bool IsDateType(DataTypeReference? dataType)
        {
            if (dataType is SqlDataTypeReference sqlDataRef)
            {
                return sqlDataRef.SqlDataTypeOption == SqlDataTypeOption.Date;
            }
            return false;
        }

        private BooleanComparisonType GetInverseComparisonType(BooleanComparisonType compType)
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

        private bool TryGetYearPattern(ScalarExpression? expr, out ColumnReferenceExpression? columnRef)
        {
            columnRef = null;

            if (expr is FunctionCall funcCall && funcCall.FunctionName != null)
            {
                string funcName = funcCall.FunctionName.Value;
                if (string.Equals(funcName, "YEAR", StringComparison.OrdinalIgnoreCase))
                {
                    if (funcCall.Parameters != null &&
                        funcCall.Parameters.Count == 1 &&
                        funcCall.Parameters[0] is ColumnReferenceExpression col)
                    {
                        columnRef = col;
                        return true;
                    }
                }
                else if (string.Equals(funcName, "DATEPART", StringComparison.OrdinalIgnoreCase))
                {
                    if (funcCall.Parameters != null &&
                        funcCall.Parameters.Count == 2)
                    {
                        var datePartParam = funcCall.Parameters[0];
                        string? datePart = null;
                        if (datePartParam is ColumnReferenceExpression colRef && colRef.MultiPartIdentifier != null && colRef.MultiPartIdentifier.Identifiers.Count > 0)
                        {
                            datePart = colRef.MultiPartIdentifier.Identifiers[^1].Value;
                        }
                        else if (datePartParam is IdentifierLiteral idLit)
                        {
                            datePart = idLit.Value;
                        }
                        else if (datePartParam is StringLiteral strLit)
                        {
                            datePart = strLit.Value;
                        }

                        if (datePart != null && (
                            string.Equals(datePart, "year", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(datePart, "yyyy", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(datePart, "yy", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (funcCall.Parameters[1] is ColumnReferenceExpression col)
                            {
                                columnRef = col;
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private bool TryOptimizeYear(
            ScalarExpression? potentialYearFunc,
            ScalarExpression? comparisonExpr,
            BooleanComparisonType comparisonType,
            out BooleanExpression? optimized)
        {
            optimized = null;

            if (TryGetYearPattern(potentialYearFunc, out var columnRef))
            {
                if (comparisonExpr is IntegerLiteral intLiteral && int.TryParse(intLiteral.Value, out int year))
                {
                    if (year < 1 || year >= 9999) return false;
                    var startLiteral = new StringLiteral { Value = $"{year}-01-01" };
                    var nextLiteral = new StringLiteral { Value = $"{year + 1}-01-01" };

                    switch (comparisonType)
                    {
                        case BooleanComparisonType.Equals:
                            optimized = new BooleanBinaryExpression
                            {
                                BinaryExpressionType = BooleanBinaryExpressionType.And,
                                FirstExpression = new BooleanComparisonExpression
                                {
                                    ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                    FirstExpression = CloneColumnReference(columnRef),
                                    SecondExpression = startLiteral
                                },
                                SecondExpression = new BooleanComparisonExpression
                                {
                                    ComparisonType = BooleanComparisonType.LessThan,
                                    FirstExpression = CloneColumnReference(columnRef),
                                    SecondExpression = nextLiteral
                                }
                            };
                            return true;

                        case BooleanComparisonType.NotEqualToBrackets:
                        case BooleanComparisonType.NotEqualToExclamation:
                            optimized = new BooleanParenthesisExpression
                            {
                                Expression = new BooleanBinaryExpression
                                {
                                    BinaryExpressionType = BooleanBinaryExpressionType.Or,
                                    FirstExpression = new BooleanComparisonExpression
                                    {
                                        ComparisonType = BooleanComparisonType.LessThan,
                                        FirstExpression = CloneColumnReference(columnRef),
                                        SecondExpression = startLiteral
                                    },
                                    SecondExpression = new BooleanComparisonExpression
                                    {
                                        ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                        FirstExpression = CloneColumnReference(columnRef),
                                        SecondExpression = nextLiteral
                                    }
                                }
                            };
                            return true;

                        case BooleanComparisonType.GreaterThan:
                            optimized = new BooleanComparisonExpression
                            {
                                ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                FirstExpression = columnRef,
                                SecondExpression = nextLiteral
                            };
                            return true;

                        case BooleanComparisonType.GreaterThanOrEqualTo:
                            optimized = new BooleanComparisonExpression
                            {
                                ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                FirstExpression = columnRef,
                                SecondExpression = startLiteral
                            };
                            return true;

                        case BooleanComparisonType.LessThan:
                            optimized = new BooleanComparisonExpression
                            {
                                ComparisonType = BooleanComparisonType.LessThan,
                                FirstExpression = columnRef,
                                SecondExpression = startLiteral
                            };
                            return true;

                        case BooleanComparisonType.LessThanOrEqualTo:
                            optimized = new BooleanComparisonExpression
                            {
                                ComparisonType = BooleanComparisonType.LessThan,
                                FirstExpression = columnRef,
                                SecondExpression = nextLiteral
                            };
                            return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetConvertOrCastDatePattern(ScalarExpression? expr, out ColumnReferenceExpression? columnRef)
        {
            columnRef = null;

            if (expr is ConvertCall convert)
            {
                if (IsDateType(convert.DataType) &&
                    convert.Parameter is ColumnReferenceExpression col)
                {
                    columnRef = col;
                    return true;
                }
            }
            else if (expr is CastCall cast)
            {
                if (IsDateType(cast.DataType) &&
                    cast.Parameter is ColumnReferenceExpression col)
                {
                    columnRef = col;
                    return true;
                }
            }

            return false;
        }

        private bool TryOptimizeConvertOrCastDate(
            ScalarExpression? potentialConvert,
            ScalarExpression? comparisonExpr,
            BooleanComparisonType comparisonType,
            out BooleanExpression? optimized)
        {
            optimized = null;

            if (TryGetConvertOrCastDatePattern(potentialConvert, out var columnRef))
            {
                if (comparisonExpr is StringLiteral stringLiteral &&
                    DateTime.TryParse(stringLiteral.Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
                {
                    if (date.Year >= 9999 || date.Year < 1 || date.TimeOfDay != TimeSpan.Zero) return false;
                    string dateStr = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    string nextDayStr = date.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

                    var startLiteral = new StringLiteral { Value = dateStr, IsNational = stringLiteral.IsNational };
                    var nextLiteral = new StringLiteral { Value = nextDayStr, IsNational = stringLiteral.IsNational };

                    switch (comparisonType)
                    {
                        case BooleanComparisonType.Equals:
                            optimized = new BooleanBinaryExpression
                            {
                                BinaryExpressionType = BooleanBinaryExpressionType.And,
                                FirstExpression = new BooleanComparisonExpression
                                {
                                    ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                    FirstExpression = CloneColumnReference(columnRef),
                                    SecondExpression = startLiteral
                                },
                                SecondExpression = new BooleanComparisonExpression
                                {
                                    ComparisonType = BooleanComparisonType.LessThan,
                                    FirstExpression = CloneColumnReference(columnRef),
                                    SecondExpression = nextLiteral
                                }
                            };
                            return true;

                        case BooleanComparisonType.NotEqualToBrackets:
                        case BooleanComparisonType.NotEqualToExclamation:
                            optimized = new BooleanParenthesisExpression
                            {
                                Expression = new BooleanBinaryExpression
                                {
                                    BinaryExpressionType = BooleanBinaryExpressionType.Or,
                                    FirstExpression = new BooleanComparisonExpression
                                    {
                                        ComparisonType = BooleanComparisonType.LessThan,
                                        FirstExpression = CloneColumnReference(columnRef),
                                        SecondExpression = startLiteral
                                    },
                                    SecondExpression = new BooleanComparisonExpression
                                    {
                                        ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                        FirstExpression = CloneColumnReference(columnRef),
                                        SecondExpression = nextLiteral
                                    }
                                }
                            };
                            return true;

                        case BooleanComparisonType.GreaterThan:
                            optimized = new BooleanComparisonExpression
                            {
                                ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                FirstExpression = columnRef,
                                SecondExpression = nextLiteral
                            };
                            return true;

                        case BooleanComparisonType.GreaterThanOrEqualTo:
                            optimized = new BooleanComparisonExpression
                            {
                                ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                FirstExpression = columnRef,
                                SecondExpression = startLiteral
                            };
                            return true;

                        case BooleanComparisonType.LessThan:
                            optimized = new BooleanComparisonExpression
                            {
                                ComparisonType = BooleanComparisonType.LessThan,
                                FirstExpression = columnRef,
                                SecondExpression = startLiteral
                            };
                            return true;

                        case BooleanComparisonType.LessThanOrEqualTo:
                            optimized = new BooleanComparisonExpression
                            {
                                ComparisonType = BooleanComparisonType.LessThan,
                                FirstExpression = columnRef,
                                SecondExpression = nextLiteral
                            };
                            return true;
                    }
                }
            }

            return false;
        }

        private bool TryOptimizeIsNull(
            ScalarExpression? potentialIsNull,
            ScalarExpression? comparisonExpr,
            BooleanComparisonType comparisonType,
            out BooleanExpression? optimized)
        {
            optimized = null;

            if (TryGetIsNullFunctionPattern(potentialIsNull, out var columnRef, out var defaultValue))
            {
                if (defaultValue != null && comparisonExpr != null)
                {
                    if (TryEvaluateComparison(defaultValue, comparisonExpr, comparisonType, out bool satisfies))
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
                                        ComparisonType = comparisonType,
                                        FirstExpression = columnRef,
                                        SecondExpression = comparisonExpr
                                    },
                                    SecondExpression = new BooleanIsNullExpression
                                    {
                                        Expression = columnRef,
                                        IsNot = true
                                    }
                                }
                            };
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
                                        ComparisonType = comparisonType,
                                        FirstExpression = columnRef,
                                        SecondExpression = comparisonExpr
                                    },
                                    SecondExpression = new BooleanIsNullExpression
                                    {
                                        Expression = columnRef,
                                        IsNot = false
                                    }
                                }
                            };
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TryEvaluateComparison(ScalarExpression? left, ScalarExpression? right, BooleanComparisonType compType, out bool result)
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

        private bool EvaluateCmpResult(int cmp, BooleanComparisonType compType)
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
                    throw new ArgumentOutOfRangeException(nameof(compType));
            }
        }

        private void FlattenAnd(BooleanExpression expr, System.Collections.Generic.List<BooleanExpression> list)
        {
            if (expr is BooleanBinaryExpression binary && binary.BinaryExpressionType == BooleanBinaryExpressionType.And)
            {
                FlattenAnd(binary.FirstExpression, list);
                FlattenAnd(binary.SecondExpression, list);
            }
            else if (expr is BooleanParenthesisExpression paren)
            {
                FlattenAnd(paren.Expression, list);
            }
            else
            {
                list.Add(expr);
            }
        }

        private void FlattenOr(BooleanExpression expr, System.Collections.Generic.List<BooleanExpression> list)
        {
            if (expr is BooleanBinaryExpression binary && binary.BinaryExpressionType == BooleanBinaryExpressionType.Or)
            {
                FlattenOr(binary.FirstExpression, list);
                FlattenOr(binary.SecondExpression, list);
            }
            else if (expr is BooleanParenthesisExpression paren)
            {
                FlattenOr(paren.Expression, list);
            }
            else
            {
                list.Add(expr);
            }
        }

        private BooleanExpression ReconstructAnd(System.Collections.Generic.List<BooleanExpression> list)
        {
            if (list.Count == 0) throw new InvalidOperationException();
            if (list.Count == 1) return list[0];

            var result = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                result = new BooleanBinaryExpression
                {
                    BinaryExpressionType = BooleanBinaryExpressionType.And,
                    FirstExpression = result,
                    SecondExpression = list[i]
                };
            }
            return result;
        }

        private BooleanExpression ReconstructOr(System.Collections.Generic.List<BooleanExpression> list)
        {
            if (list.Count == 0) throw new InvalidOperationException();
            if (list.Count == 1) return list[0];

            var result = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                result = new BooleanBinaryExpression
                {
                    BinaryExpressionType = BooleanBinaryExpressionType.Or,
                    FirstExpression = result,
                    SecondExpression = list[i]
                };
            }
            return result;
        }

        private System.Collections.Generic.List<BooleanExpression> DeduplicateExpressions(System.Collections.Generic.List<BooleanExpression> list)
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = new System.Collections.Generic.List<BooleanExpression>();
            foreach (var expr in list)
            {
                string formatted = FormatFragment(expr);
                if (seen.Add(formatted))
                {
                    unique.Add(expr);
                }
            }
            return unique;
        }

        private string FormatFragment(TSqlFragment fragment)
        {
            var generator = new Sql160ScriptGenerator();
            generator.GenerateScript(fragment, out string script);
            return script.Trim();
        }

        private bool TryEvaluateStaticBoolean(BooleanExpression? expr, out bool result)
        {
            result = false;
            if (expr == null) return false;

            if (expr is BooleanComparisonExpression comp)
            {
                if (TryEvaluateComparison(comp.FirstExpression, comp.SecondExpression, comp.ComparisonType, out bool val))
                {
                    result = val;
                    return true;
                }
                return false;
            }

            if (expr is BooleanIsNullExpression isNull)
            {
                if (isNull.Expression is NullLiteral)
                {
                    result = !isNull.IsNot;
                    return true;
                }
                else if (IsLiteralOrConstant(isNull.Expression))
                {
                    result = isNull.IsNot;
                    return true;
                }
                return false;
            }

            if (expr is BooleanNotExpression notExpr)
            {
                if (TryEvaluateStaticBoolean(notExpr.Expression, out bool innerVal))
                {
                    result = !innerVal;
                    return true;
                }
                return false;
            }

            if (expr is BooleanParenthesisExpression paren)
            {
                return TryEvaluateStaticBoolean(paren.Expression, out result);
            }

            if (expr is BooleanBinaryExpression binary)
            {
                if (TryEvaluateStaticBoolean(binary.FirstExpression, out bool leftVal) &&
                    TryEvaluateStaticBoolean(binary.SecondExpression, out bool rightVal))
                {
                    if (binary.BinaryExpressionType == BooleanBinaryExpressionType.And)
                    {
                        result = leftVal && rightVal;
                        return true;
                    }
                    else if (binary.BinaryExpressionType == BooleanBinaryExpressionType.Or)
                    {
                        result = leftVal || rightVal;
                        return true;
                    }
                }
                return false;
            }

            return false;
        }

        private bool TryOptimizeDateAdd(
            ScalarExpression? potentialDateAdd,
            ScalarExpression? comparisonExpr,
            BooleanComparisonType comparisonType,
            out BooleanExpression? optimized)
        {
            optimized = null;

            if (comparisonExpr == null) return false;

            if (TryGetDateAddPattern(potentialDateAdd, out string? datePart, out int number, out var columnRef))
            {
                if (number == int.MinValue) return false;

                if (comparisonExpr is StringLiteral strLit &&
                    DateTime.TryParse(strLit.Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var baseDate))
                {
                    try
                    {
                        DateTime targetDate;
                        int offset = -number;
                        if (string.Equals(datePart, "day", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(datePart, "dd", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(datePart, "d", StringComparison.OrdinalIgnoreCase))
                        {
                            targetDate = baseDate.AddDays(offset);
                        }
                        else if (string.Equals(datePart, "month", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(datePart, "mm", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(datePart, "m", StringComparison.OrdinalIgnoreCase))
                        {
                            targetDate = baseDate.AddMonths(offset);
                        }
                        else if (string.Equals(datePart, "year", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(datePart, "yyyy", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(datePart, "yy", StringComparison.OrdinalIgnoreCase))
                        {
                            targetDate = baseDate.AddYears(offset);
                        }
                        else
                        {
                            targetDate = baseDate;
                        }

                        if (targetDate.Year >= 9999 || targetDate.Year < 1)
                        {
                            return false; // Prevent runtime overflow
                        }
                    }
                    catch
                    {
                        return false; // Calculation threw exception, unsafe to optimize
                    }
                }

                var datePartParam = ((FunctionCall)potentialDateAdd!).Parameters[0];
                var negatedNumberExpr = ConstructIntegerExpression(-number);

                var dateAddFunc = new FunctionCall
                {
                    FunctionName = new Identifier { Value = "DATEADD" }
                };
                dateAddFunc.Parameters.Add(datePartParam);
                dateAddFunc.Parameters.Add(negatedNumberExpr);
                dateAddFunc.Parameters.Add(comparisonExpr!);

                optimized = new BooleanComparisonExpression
                {
                    ComparisonType = comparisonType,
                    FirstExpression = columnRef,
                    SecondExpression = dateAddFunc
                };
                return true;
            }

            return false;
        }

        private bool TryGetDateAddPattern(
            ScalarExpression? expr,
            out string? datePart,
            out int number,
            out ColumnReferenceExpression? columnRef)
        {
            datePart = null;
            number = 0;
            columnRef = null;

            if (expr is FunctionCall funcCall && funcCall.FunctionName != null)
            {
                string funcName = funcCall.FunctionName.Value;
                if (string.Equals(funcName, "DATEADD", StringComparison.OrdinalIgnoreCase))
                {
                    if (funcCall.Parameters != null && funcCall.Parameters.Count == 3)
                    {
                        // 1. Parse datePart
                        var datePartParam = funcCall.Parameters[0];
                        if (datePartParam is ColumnReferenceExpression colRef && colRef.MultiPartIdentifier != null && colRef.MultiPartIdentifier.Identifiers.Count > 0)
                        {
                            datePart = colRef.MultiPartIdentifier.Identifiers[^1].Value;
                        }
                        else if (datePartParam is IdentifierLiteral idLit)
                        {
                            datePart = idLit.Value;
                        }
                        else if (datePartParam is StringLiteral strLit)
                        {
                            datePart = strLit.Value;
                        }

                        if (string.IsNullOrEmpty(datePart)) return false;

                        // 2. Parse number
                        var numberParam = funcCall.Parameters[1];
                        int parsedNumber = 0;
                        bool parsed = false;

                        if (numberParam is IntegerLiteral intLit)
                        {
                            parsed = int.TryParse(intLit.Value, out parsedNumber);
                        }
                        else if (numberParam is UnaryExpression unary && unary.UnaryExpressionType == UnaryExpressionType.Negative)
                        {
                            if (unary.Expression is IntegerLiteral innerInt)
                            {
                                parsed = int.TryParse(innerInt.Value, out int temp);
                                parsedNumber = -temp;
                            }
                        }

                        if (!parsed) return false;

                        // 3. Parse columnRef
                        if (funcCall.Parameters[2] is ColumnReferenceExpression col)
                        {
                            columnRef = col;
                            number = parsedNumber;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private ScalarExpression ConstructIntegerExpression(int val)
        {
            if (val < 0)
            {
                return new UnaryExpression
                {
                    UnaryExpressionType = UnaryExpressionType.Negative,
                    Expression = new IntegerLiteral { Value = (-val).ToString() }
                };
            }
            return new IntegerLiteral { Value = val.ToString() };
        }

        private bool TryOptimizeDateDiff(
            ScalarExpression? potentialDateDiff,
            ScalarExpression? comparisonExpr,
            BooleanComparisonType comparisonType,
            out BooleanExpression? optimized)
        {
            optimized = null;

            if (TryGetDateDiffPattern(potentialDateDiff, out var datePart, out var columnRef, out var valueLiteral, out bool isColumnFirst))
            {
                // The difference value must be an integer literal
                int diff = 0;
                bool parsedDiff = false;
                if (comparisonExpr is IntegerLiteral intLit)
                {
                    parsedDiff = int.TryParse(intLit.Value, out diff);
                }
                else if (comparisonExpr is UnaryExpression unary && unary.UnaryExpressionType == UnaryExpressionType.Negative)
                {
                    if (unary.Expression is IntegerLiteral innerInt)
                    {
                        parsedDiff = int.TryParse(innerInt.Value, out int temp);
                        diff = -temp;
                    }
                }

                if (!parsedDiff) return false;

                // Parse the base date
                if (valueLiteral!.Value == null || !DateTime.TryParse(valueLiteral.Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dateVal))
                {
                    return false;
                }

                // Calculate baseDate and baseDate + 1 unit
                DateTime baseDate;
                DateTime nextDate;

                // datepart options:
                bool isDay = string.Equals(datePart, "day", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(datePart, "dd", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(datePart, "d", StringComparison.OrdinalIgnoreCase);

                bool isMonth = string.Equals(datePart, "month", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(datePart, "mm", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(datePart, "m", StringComparison.OrdinalIgnoreCase);

                bool isYear = string.Equals(datePart, "year", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(datePart, "yyyy", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(datePart, "yy", StringComparison.OrdinalIgnoreCase);

                int multiplier = isColumnFirst ? -1 : 1;
                if (diff == int.MinValue && multiplier == -1) return false;
                int offset = diff * multiplier;

                try
                {
                    if (isDay)
                    {
                        baseDate = dateVal.AddDays(offset).Date;
                        nextDate = baseDate.AddDays(1);
                    }
                    else if (isMonth)
                    {
                        var tempDate = dateVal.AddMonths(offset);
                        baseDate = new DateTime(tempDate.Year, tempDate.Month, 1);
                        nextDate = baseDate.AddMonths(1);
                    }
                    else if (isYear)
                    {
                        var tempDate = dateVal.AddYears(offset);
                        baseDate = new DateTime(tempDate.Year, 1, 1);
                        nextDate = baseDate.AddYears(1);
                    }
                    else
                    {
                        return false;
                    }

                    if (baseDate.Year >= 9999 || baseDate.Year < 1 || nextDate.Year >= 9999 || nextDate.Year < 1)
                    {
                        return false;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }

                var startLiteral = new StringLiteral { Value = baseDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), IsNational = valueLiteral.IsNational };
                var nextLiteral = new StringLiteral { Value = nextDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), IsNational = valueLiteral.IsNational };

                if (isColumnFirst)
                {
                    switch (comparisonType)
                    {
                        case BooleanComparisonType.Equals:
                            optimized = CreateRangeExpression(columnRef!, startLiteral, nextLiteral);
                            return true;

                        case BooleanComparisonType.GreaterThan:
                            optimized = CreateComparisonExpression(columnRef!, startLiteral, BooleanComparisonType.LessThan);
                            return true;

                        case BooleanComparisonType.GreaterThanOrEqualTo:
                            optimized = CreateComparisonExpression(columnRef!, nextLiteral, BooleanComparisonType.LessThan);
                            return true;

                        case BooleanComparisonType.LessThan:
                            optimized = CreateComparisonExpression(columnRef!, nextLiteral, BooleanComparisonType.GreaterThanOrEqualTo);
                            return true;

                        case BooleanComparisonType.LessThanOrEqualTo:
                            optimized = CreateComparisonExpression(columnRef!, startLiteral, BooleanComparisonType.GreaterThanOrEqualTo);
                            return true;

                        case BooleanComparisonType.NotEqualToBrackets:
                        case BooleanComparisonType.NotEqualToExclamation:
                            optimized = CreateNotEqualsRangeExpression(columnRef!, startLiteral, nextLiteral);
                            return true;
                    }
                }
                else
                {
                    switch (comparisonType)
                    {
                        case BooleanComparisonType.Equals:
                            optimized = CreateRangeExpression(columnRef!, startLiteral, nextLiteral);
                            return true;

                        case BooleanComparisonType.GreaterThan:
                            optimized = CreateComparisonExpression(columnRef!, nextLiteral, BooleanComparisonType.GreaterThanOrEqualTo);
                            return true;

                        case BooleanComparisonType.GreaterThanOrEqualTo:
                            optimized = CreateComparisonExpression(columnRef!, startLiteral, BooleanComparisonType.GreaterThanOrEqualTo);
                            return true;

                        case BooleanComparisonType.LessThan:
                            optimized = CreateComparisonExpression(columnRef!, startLiteral, BooleanComparisonType.LessThan);
                            return true;

                        case BooleanComparisonType.LessThanOrEqualTo:
                            optimized = CreateComparisonExpression(columnRef!, nextLiteral, BooleanComparisonType.LessThan);
                            return true;

                        case BooleanComparisonType.NotEqualToBrackets:
                        case BooleanComparisonType.NotEqualToExclamation:
                            optimized = CreateNotEqualsRangeExpression(columnRef!, startLiteral, nextLiteral);
                            return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetDateDiffPattern(
            ScalarExpression? expr,
            out string? datePart,
            out ColumnReferenceExpression? columnRef,
            out StringLiteral? valueLiteral,
            out bool isColumnFirst)
        {
            datePart = null;
            columnRef = null;
            valueLiteral = null;
            isColumnFirst = false;

            if (expr is FunctionCall funcCall && funcCall.FunctionName != null)
            {
                string funcName = funcCall.FunctionName.Value;
                if (string.Equals(funcName, "DATEDIFF", StringComparison.OrdinalIgnoreCase))
                {
                    if (funcCall.Parameters != null && funcCall.Parameters.Count == 3)
                    {
                        if (TryGetDateDiffDatePart(funcCall.Parameters[0], out var dp))
                        {
                            if (string.Equals(dp, "day", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dp, "dd", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dp, "d", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dp, "month", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dp, "mm", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dp, "m", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dp, "year", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dp, "yyyy", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dp, "yy", StringComparison.OrdinalIgnoreCase))
                            {
                                datePart = dp;

                                var param1 = funcCall.Parameters[1];
                                var param2 = funcCall.Parameters[2];

                                if (param1 is ColumnReferenceExpression col1 && param2 is StringLiteral str2)
                                {
                                    columnRef = col1;
                                    valueLiteral = str2;
                                    isColumnFirst = true;
                                    return true;
                                }
                                else if (param1 is StringLiteral str1 && param2 is ColumnReferenceExpression col2)
                                {
                                    columnRef = col2;
                                    valueLiteral = str1;
                                    isColumnFirst = false;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        private bool TryGetDateDiffDatePart(ScalarExpression datePartParam, out string? datePart)
        {
            datePart = null;
            if (datePartParam is ColumnReferenceExpression colRef && colRef.MultiPartIdentifier != null && colRef.MultiPartIdentifier.Identifiers.Count > 0)
            {
                datePart = colRef.MultiPartIdentifier.Identifiers[^1].Value;
            }
            else if (datePartParam is IdentifierLiteral idLit)
            {
                datePart = idLit.Value;
            }
            else if (datePartParam is StringLiteral strLit)
            {
                datePart = strLit.Value;
            }
            return !string.IsNullOrEmpty(datePart);
        }

        private BooleanExpression CreateRangeExpression(ColumnReferenceExpression columnRef, StringLiteral start, StringLiteral end)
        {
            return new BooleanBinaryExpression
            {
                BinaryExpressionType = BooleanBinaryExpressionType.And,
                FirstExpression = new BooleanComparisonExpression
                {
                    ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                    FirstExpression = columnRef,
                    SecondExpression = start
                },
                SecondExpression = new BooleanComparisonExpression
                {
                    ComparisonType = BooleanComparisonType.LessThan,
                    FirstExpression = columnRef,
                    SecondExpression = end
                }
            };
        }

        private BooleanExpression CreateNotEqualsRangeExpression(ColumnReferenceExpression columnRef, StringLiteral start, StringLiteral end)
        {
            return new BooleanParenthesisExpression
            {
                Expression = new BooleanBinaryExpression
                {
                    BinaryExpressionType = BooleanBinaryExpressionType.Or,
                    FirstExpression = new BooleanComparisonExpression
                    {
                        ComparisonType = BooleanComparisonType.LessThan,
                        FirstExpression = columnRef,
                        SecondExpression = start
                    },
                    SecondExpression = new BooleanComparisonExpression
                    {
                        ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                        FirstExpression = columnRef,
                        SecondExpression = end
                    }
                }
            };
        }

        private BooleanExpression CreateComparisonExpression(ColumnReferenceExpression columnRef, StringLiteral val, BooleanComparisonType compType)
        {
            return new BooleanComparisonExpression
            {
                ComparisonType = compType,
                FirstExpression = columnRef,
                SecondExpression = val
            };
        }

        private bool TryOptimizeAbs(
            ScalarExpression? potentialAbs,
            ScalarExpression? comparisonExpr,
            BooleanComparisonType comparisonType,
            out BooleanExpression? optimized)
        {
            optimized = null;

            if (TryGetAbsPattern(potentialAbs, out var columnRef))
            {
                if (TryGetNumericValue(comparisonExpr, out decimal c))
                {
                    if (c < 0)
                    {
                        switch (comparisonType)
                        {
                            case BooleanComparisonType.Equals:
                            case BooleanComparisonType.LessThan:
                            case BooleanComparisonType.LessThanOrEqualTo:
                                optimized = CreateNullPropagatingFalse(columnRef!, comparisonExpr!);
                                return true;
                            case BooleanComparisonType.NotEqualToBrackets:
                            case BooleanComparisonType.NotEqualToExclamation:
                            case BooleanComparisonType.GreaterThan:
                            case BooleanComparisonType.GreaterThanOrEqualTo:
                                optimized = CreateNullPropagatingTrue(columnRef!);
                                return true;
                        }
                    }
                    else if (c == 0)
                    {
                        switch (comparisonType)
                        {
                            case BooleanComparisonType.Equals:
                            case BooleanComparisonType.LessThanOrEqualTo:
                                optimized = new BooleanComparisonExpression
                                {
                                    ComparisonType = BooleanComparisonType.Equals,
                                    FirstExpression = columnRef,
                                    SecondExpression = comparisonExpr
                                };
                                return true;
                            case BooleanComparisonType.NotEqualToBrackets:
                            case BooleanComparisonType.NotEqualToExclamation:
                            case BooleanComparisonType.GreaterThan:
                                optimized = new BooleanComparisonExpression
                                {
                                    ComparisonType = BooleanComparisonType.NotEqualToBrackets,
                                    FirstExpression = columnRef,
                                    SecondExpression = comparisonExpr
                                };
                                return true;
                            case BooleanComparisonType.GreaterThanOrEqualTo:
                                optimized = CreateNullPropagatingTrue(columnRef!);
                                return true;
                            case BooleanComparisonType.LessThan:
                                optimized = CreateNullPropagatingFalse(columnRef!, comparisonExpr!);
                                return true;
                        }
                    }
                    else // c > 0
                    {
                        var posLiteral = comparisonExpr;
                        var negLiteral = ConstructNegativeLiteral(posLiteral!);

                        switch (comparisonType)
                        {
                            case BooleanComparisonType.Equals:
                                optimized = new BooleanParenthesisExpression
                                {
                                    Expression = new BooleanBinaryExpression
                                    {
                                        BinaryExpressionType = BooleanBinaryExpressionType.Or,
                                        FirstExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.Equals,
                                            FirstExpression = columnRef,
                                            SecondExpression = posLiteral
                                        },
                                        SecondExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.Equals,
                                            FirstExpression = columnRef,
                                            SecondExpression = negLiteral
                                        }
                                    }
                                };
                                return true;

                            case BooleanComparisonType.NotEqualToBrackets:
                            case BooleanComparisonType.NotEqualToExclamation:
                                optimized = new BooleanParenthesisExpression
                                {
                                    Expression = new BooleanBinaryExpression
                                    {
                                        BinaryExpressionType = BooleanBinaryExpressionType.And,
                                        FirstExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.NotEqualToBrackets,
                                            FirstExpression = columnRef,
                                            SecondExpression = posLiteral
                                        },
                                        SecondExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.NotEqualToBrackets,
                                            FirstExpression = columnRef,
                                            SecondExpression = negLiteral
                                        }
                                    }
                                };
                                return true;

                            case BooleanComparisonType.GreaterThan:
                                optimized = new BooleanParenthesisExpression
                                {
                                    Expression = new BooleanBinaryExpression
                                    {
                                        BinaryExpressionType = BooleanBinaryExpressionType.Or,
                                        FirstExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.GreaterThan,
                                            FirstExpression = columnRef,
                                            SecondExpression = posLiteral
                                        },
                                        SecondExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.LessThan,
                                            FirstExpression = columnRef,
                                            SecondExpression = negLiteral
                                        }
                                    }
                                };
                                return true;

                            case BooleanComparisonType.GreaterThanOrEqualTo:
                                optimized = new BooleanParenthesisExpression
                                {
                                    Expression = new BooleanBinaryExpression
                                    {
                                        BinaryExpressionType = BooleanBinaryExpressionType.Or,
                                        FirstExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                            FirstExpression = columnRef,
                                            SecondExpression = posLiteral
                                        },
                                        SecondExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.LessThanOrEqualTo,
                                            FirstExpression = columnRef,
                                            SecondExpression = negLiteral
                                        }
                                    }
                                };
                                return true;

                            case BooleanComparisonType.LessThan:
                                optimized = new BooleanParenthesisExpression
                                {
                                    Expression = new BooleanBinaryExpression
                                    {
                                        BinaryExpressionType = BooleanBinaryExpressionType.And,
                                        FirstExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.LessThan,
                                            FirstExpression = columnRef,
                                            SecondExpression = posLiteral
                                        },
                                        SecondExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.GreaterThan,
                                            FirstExpression = columnRef,
                                            SecondExpression = negLiteral
                                        }
                                    }
                                };
                                return true;

                            case BooleanComparisonType.LessThanOrEqualTo:
                                optimized = new BooleanParenthesisExpression
                                {
                                    Expression = new BooleanBinaryExpression
                                    {
                                        BinaryExpressionType = BooleanBinaryExpressionType.And,
                                        FirstExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.LessThanOrEqualTo,
                                            FirstExpression = columnRef,
                                            SecondExpression = posLiteral
                                        },
                                        SecondExpression = new BooleanComparisonExpression
                                        {
                                            ComparisonType = BooleanComparisonType.GreaterThanOrEqualTo,
                                            FirstExpression = columnRef,
                                            SecondExpression = negLiteral
                                        }
                                    }
                                };
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TryGetAbsPattern(ScalarExpression? expr, out ColumnReferenceExpression? columnRef)
        {
            columnRef = null;
            if (expr is FunctionCall funcCall && funcCall.FunctionName != null)
            {
                string funcName = funcCall.FunctionName.Value;
                if (string.Equals(funcName, "ABS", StringComparison.OrdinalIgnoreCase))
                {
                    if (funcCall.Parameters != null &&
                        funcCall.Parameters.Count == 1 &&
                        funcCall.Parameters[0] is ColumnReferenceExpression col)
                    {
                        columnRef = col;
                        return true;
                    }
                }
            }
            return false;
        }

        private ScalarExpression ConstructNegativeLiteral(ScalarExpression posExpr)
        {
            if (posExpr is UnaryExpression unary && unary.UnaryExpressionType == UnaryExpressionType.Negative)
            {
                return unary.Expression;
            }

            return new UnaryExpression
            {
                UnaryExpressionType = UnaryExpressionType.Negative,
                Expression = posExpr
            };
        }

        private BooleanExpression CreateStaticBoolean(bool value)
        {
            return new BooleanComparisonExpression
            {
                ComparisonType = BooleanComparisonType.Equals,
                FirstExpression = new IntegerLiteral { Value = "1" },
                SecondExpression = new IntegerLiteral { Value = value ? "1" : "0" }
            };
        }

        private BooleanExpression CreateNullPropagatingFalse(ScalarExpression columnRef, ScalarExpression comparisonExpr)
        {
            var negExpr = comparisonExpr;
            ScalarExpression posExpr;
            if (comparisonExpr is UnaryExpression unary && unary.UnaryExpressionType == UnaryExpressionType.Negative)
            {
                posExpr = unary.Expression;
            }
            else
            {
                posExpr = comparisonExpr;
            }

            return new BooleanBinaryExpression
            {
                BinaryExpressionType = BooleanBinaryExpressionType.And,
                FirstExpression = new BooleanComparisonExpression
                {
                    ComparisonType = BooleanComparisonType.LessThan,
                    FirstExpression = columnRef,
                    SecondExpression = negExpr
                },
                SecondExpression = new BooleanComparisonExpression
                {
                    ComparisonType = BooleanComparisonType.GreaterThan,
                    FirstExpression = columnRef,
                    SecondExpression = posExpr
                }
            };
        }

        private BooleanExpression CreateNullPropagatingTrue(ScalarExpression columnRef)
        {
            return new BooleanComparisonExpression
            {
                ComparisonType = BooleanComparisonType.Equals,
                FirstExpression = columnRef,
                SecondExpression = columnRef
            };
        }

        private bool TryOptimizeRTrim(
            ScalarExpression? potentialRTrim,
            ScalarExpression? comparisonExpr,
            BooleanComparisonType comparisonType,
            out BooleanExpression? optimized)
        {
            optimized = null;

            if (comparisonExpr is StringLiteral)
            {
                if (TryGetRTrimPattern(potentialRTrim, out var columnRef))
                {
                    if (comparisonType == BooleanComparisonType.Equals ||
                        comparisonType == BooleanComparisonType.NotEqualToBrackets ||
                        comparisonType == BooleanComparisonType.NotEqualToExclamation)
                    {
                        optimized = new BooleanComparisonExpression
                        {
                            ComparisonType = comparisonType,
                            FirstExpression = columnRef,
                            SecondExpression = comparisonExpr
                        };
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetRTrimPattern(ScalarExpression? expr, out ColumnReferenceExpression? columnRef)
        {
            columnRef = null;
            if (expr is FunctionCall funcCall && funcCall.FunctionName != null)
            {
                string funcName = funcCall.FunctionName.Value;
                if (string.Equals(funcName, "RTRIM", StringComparison.OrdinalIgnoreCase))
                {
                    if (funcCall.Parameters != null &&
                        funcCall.Parameters.Count == 1 &&
                        funcCall.Parameters[0] is ColumnReferenceExpression col)
                    {
                        columnRef = col;
                        return true;
                    }
                }
            }
            return false;
        }

        private ColumnReferenceExpression CloneColumnReference(ColumnReferenceExpression? original)
        {
            if (original == null) return null!;
            var clone = new ColumnReferenceExpression
            {
                Collation = original.Collation,
                ColumnType = original.ColumnType
            };
            if (original.MultiPartIdentifier != null)
            {
                clone.MultiPartIdentifier = new MultiPartIdentifier();
                foreach (var id in original.MultiPartIdentifier.Identifiers)
                {
                    clone.MultiPartIdentifier.Identifiers.Add(new Identifier { Value = id.Value, QuoteType = id.QuoteType });
                }
            }
            return clone;
        }
    }
}
