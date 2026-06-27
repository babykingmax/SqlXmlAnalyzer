using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum SqlDiffTokenKind
    {
        Comment,
        StringLiteral,
        Keyword,
        Identifier,
        Whitespace,
        Other
    }

    public sealed record SqlAlignedLines(
        IReadOnlyList<string?> Original,
        IReadOnlyList<string?> Refactored);

    public sealed record SqlDiffToken(
        string Text,
        SqlDiffTokenKind Kind,
        int Start,
        int Length);

    public sealed class SqlDiffService
    {
        private static readonly HashSet<string> SqlKeywords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "ON", "GROUP", "BY", "ORDER",
                "HAVING", "AND", "OR", "NOT", "IN", "EXISTS", "LIKE", "AS", "CREATE", "INDEX", "DROP", "TABLE",
                "INSERT", "UPDATE", "DELETE", "INTO", "VALUES", "SET", "EXEC", "PROCEDURE", "DECLARE", "WITH",
                "UNION", "ALL", "CASE", "WHEN", "THEN", "ELSE", "END", "NULL", "IS", "CAST", "CONVERT", "GO",
                "CROSS", "APPLY", "TOP", "DISTINCT"
            };

        private static readonly Regex SqlTokenizerRegex =
            new(
                @"(--.*)|('[^']*(?:''[^']*)*')|([a-zA-Z_#@][a-zA-Z0-9_]*)|(\s+)|(.)",
                RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(100));

        private static readonly Regex WhitespaceRegex =
            new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

        public SqlAlignedLines AlignLines(
            IReadOnlyList<string> originalLines,
            IReadOnlyList<string> refactoredLines)
        {
            if (originalLines == null)
            {
                throw new ArgumentNullException(nameof(originalLines));
            }

            if (refactoredLines == null)
            {
                throw new ArgumentNullException(nameof(refactoredLines));
            }

            int originalCount = originalLines.Count;
            int refactoredCount = refactoredLines.Count;

            if (originalCount > 1000 || refactoredCount > 1000)
            {
                return AlignByPosition(originalLines, refactoredLines);
            }

            int[,] dp = BuildLcsMatrix(originalLines, refactoredLines);
            var alignedOriginal = new List<string?>();
            var alignedRefactored = new List<string?>();
            int currOriginal = originalCount;
            int currRefactored = refactoredCount;

            while (currOriginal > 0 || currRefactored > 0)
            {
                if (currOriginal > 0 &&
                    currRefactored > 0 &&
                    IsEquivalentForDiff(
                        originalLines[currOriginal - 1],
                        refactoredLines[currRefactored - 1]))
                {
                    alignedOriginal.Add(originalLines[currOriginal - 1]);
                    alignedRefactored.Add(refactoredLines[currRefactored - 1]);
                    currOriginal--;
                    currRefactored--;
                }
                else if (currRefactored > 0 &&
                    (currOriginal == 0 || dp[currOriginal, currRefactored - 1] >= dp[currOriginal - 1, currRefactored]))
                {
                    alignedOriginal.Add(null);
                    alignedRefactored.Add(refactoredLines[currRefactored - 1]);
                    currRefactored--;
                }
                else
                {
                    alignedOriginal.Add(originalLines[currOriginal - 1]);
                    alignedRefactored.Add(null);
                    currOriginal--;
                }
            }

            alignedOriginal.Reverse();
            alignedRefactored.Reverse();
            PairAdjacentSingleLineChanges(alignedOriginal, alignedRefactored);
            return new SqlAlignedLines(alignedOriginal, alignedRefactored);
        }

        public bool IsEquivalentForDiff(string? left, string? right)
        {
            return NormalizeForDiff(left) == NormalizeForDiff(right);
        }

        public IReadOnlyList<SqlDiffToken> TokenizeLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<SqlDiffToken>();
            }

            try
            {
                return SqlTokenizerRegex
                    .Matches(text)
                    .Cast<Match>()
                    .Select(ToToken)
                    .ToList();
            }
            catch (RegexMatchTimeoutException)
            {
                return new[] { new SqlDiffToken(text, SqlDiffTokenKind.Other, 0, text.Length) };
            }
        }

        public IReadOnlyList<int> GetLineStartOffsets(string sql)
        {
            var offsets = new List<int>();
            if (string.IsNullOrEmpty(sql))
            {
                return offsets;
            }

            offsets.Add(0);
            for (int i = 0; i < sql.Length; i++)
            {
                if (sql[i] == '\r')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\n')
                    {
                        i++;
                    }

                    offsets.Add(i + 1);
                }
                else if (sql[i] == '\n')
                {
                    offsets.Add(i + 1);
                }
            }

            return offsets;
        }

        private static int[,] BuildLcsMatrix(
            IReadOnlyList<string> originalLines,
            IReadOnlyList<string> refactoredLines)
        {
            int[,] dp = new int[originalLines.Count + 1, refactoredLines.Count + 1];

            for (int i = 1; i <= originalLines.Count; i++)
            {
                for (int j = 1; j <= refactoredLines.Count; j++)
                {
                    if (NormalizeForDiff(originalLines[i - 1]) == NormalizeForDiff(refactoredLines[j - 1]))
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    }
                    else
                    {
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                    }
                }
            }

            return dp;
        }

        private static SqlAlignedLines AlignByPosition(
            IReadOnlyList<string> originalLines,
            IReadOnlyList<string> refactoredLines)
        {
            var alignedOriginal = new List<string?>();
            var alignedRefactored = new List<string?>();
            int minLength = Math.Min(originalLines.Count, refactoredLines.Count);

            for (int i = 0; i < minLength; i++)
            {
                alignedOriginal.Add(originalLines[i]);
                alignedRefactored.Add(refactoredLines[i]);
            }

            for (int i = minLength; i < originalLines.Count; i++)
            {
                alignedOriginal.Add(originalLines[i]);
                alignedRefactored.Add(null);
            }

            for (int i = minLength; i < refactoredLines.Count; i++)
            {
                alignedOriginal.Add(null);
                alignedRefactored.Add(refactoredLines[i]);
            }

            return new SqlAlignedLines(alignedOriginal, alignedRefactored);
        }

        private static void PairAdjacentSingleLineChanges(
            List<string?> alignedOriginal,
            List<string?> alignedRefactored)
        {
            for (int i = 0; i < alignedOriginal.Count - 1; i++)
            {
                if (alignedOriginal[i] != null &&
                    alignedRefactored[i] == null &&
                    alignedOriginal[i + 1] == null &&
                    alignedRefactored[i + 1] != null)
                {
                    alignedRefactored[i] = alignedRefactored[i + 1];
                    alignedOriginal.RemoveAt(i + 1);
                    alignedRefactored.RemoveAt(i + 1);
                }
                else if (alignedOriginal[i] == null &&
                    alignedRefactored[i] != null &&
                    alignedOriginal[i + 1] != null &&
                    alignedRefactored[i + 1] == null)
                {
                    alignedOriginal[i] = alignedOriginal[i + 1];
                    alignedOriginal.RemoveAt(i + 1);
                    alignedRefactored.RemoveAt(i + 1);
                }
            }
        }

        private static string NormalizeForDiff(string? value)
        {
            if (value == null)
            {
                return "";
            }

            return WhitespaceRegex.Replace(value, "").ToLowerInvariant();
        }

        private static SqlDiffToken ToToken(Match match)
        {
            SqlDiffTokenKind kind;
            if (match.Groups[1].Success)
            {
                kind = SqlDiffTokenKind.Comment;
            }
            else if (match.Groups[2].Success)
            {
                kind = SqlDiffTokenKind.StringLiteral;
            }
            else if (match.Groups[3].Success)
            {
                kind = SqlKeywords.Contains(match.Value)
                    ? SqlDiffTokenKind.Keyword
                    : SqlDiffTokenKind.Identifier;
            }
            else if (match.Groups[4].Success)
            {
                kind = SqlDiffTokenKind.Whitespace;
            }
            else
            {
                kind = SqlDiffTokenKind.Other;
            }

            return new SqlDiffToken(match.Value, kind, match.Index, match.Length);
        }
    }
}
