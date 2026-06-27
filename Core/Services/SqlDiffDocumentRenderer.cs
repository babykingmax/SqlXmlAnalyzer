using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Refactoring.Rules;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class SqlDiffDocumentRenderer
    {
        private static readonly Brush AdditionBrush = CreateFrozenBrush(Color.FromRgb(232, 245, 233));
        private static readonly Brush DeletionBrush = CreateFrozenBrush(Color.FromRgb(255, 235, 238));
        private static readonly Brush ModificationBrush = CreateFrozenBrush(Color.FromRgb(227, 242, 253));
        private static readonly Brush PlaceholderBrush = CreateFrozenBrush(Color.FromRgb(245, 245, 245));
        private static readonly TextDecorationCollection SquigglyUnderline = CreateSquigglyUnderline();

        private readonly SqlDiffService _sqlDiffService;

        public SqlDiffDocumentRenderer(SqlDiffService sqlDiffService)
        {
            _sqlDiffService = sqlDiffService ?? throw new ArgumentNullException(nameof(sqlDiffService));
        }

        public void Render(
            RichTextBox target,
            IReadOnlyList<string?> lines,
            bool isRefactoredSide,
            IReadOnlyList<string?> opposingLines,
            string originalSql,
            Func<ScalarSubquery, UIElement>? quickFixElementFactory = null)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (lines == null)
            {
                throw new ArgumentNullException(nameof(lines));
            }

            if (opposingLines == null)
            {
                throw new ArgumentNullException(nameof(opposingLines));
            }

            target.Document.Blocks.Clear();
            target.BeginChange();
            try
            {
                IReadOnlyList<ScalarSubquery>? subqueries = null;
                IReadOnlyList<int>? lineStartOffsets = null;
                HashSet<ScalarSubquery>? handledSubqueries = null;

                if (!isRefactoredSide && !string.IsNullOrEmpty(originalSql))
                {
                    subqueries = ScalarSubqueryToJoinRule.GetRewriteableSubqueries(originalSql);
                    lineStartOffsets = _sqlDiffService.GetLineStartOffsets(originalSql);
                    handledSubqueries = new HashSet<ScalarSubquery>();
                }

                int realLineIndex = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    string? line = lines[i];
                    string? opposingLine = i < opposingLines.Count ? opposingLines[i] : null;
                    Paragraph paragraph = CreateParagraph();

                    if (line == null)
                    {
                        paragraph.Background = PlaceholderBrush;
                        paragraph.Inlines.Add(new Run(" ") { Foreground = Brushes.Transparent });
                    }
                    else
                    {
                        int lineStartOffset = GetLineStartOffset(
                            isRefactoredSide,
                            lineStartOffsets,
                            realLineIndex);
                        ApplyLineBackground(paragraph, line, opposingLine, isRefactoredSide);
                        AddFormattedSqlLine(
                            paragraph,
                            line,
                            subqueries,
                            lineStartOffset,
                            handledSubqueries,
                            quickFixElementFactory);

                        if (!isRefactoredSide)
                        {
                            realLineIndex++;
                        }
                    }

                    target.Document.Blocks.Add(paragraph);
                }
            }
            finally
            {
                target.EndChange();
            }
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static TextDecorationCollection CreateSquigglyUnderline()
        {
            var brush = new DrawingBrush
            {
                Viewport = new Rect(0, 0, 6, 4),
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.Tile
            };

            var path = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(0, 2) };
            figure.Segments.Add(new BezierSegment(new Point(1.5, 0), new Point(1.5, 4), new Point(3, 2), true));
            figure.Segments.Add(new BezierSegment(new Point(4.5, 0), new Point(4.5, 4), new Point(6, 2), true));
            path.Figures.Add(figure);
            brush.Drawing = new GeometryDrawing(null, new Pen(Brushes.Red, 1.2), path);

            var decorations = new TextDecorationCollection
            {
                new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Pen = new Pen(brush, 3)
                }
            };
            decorations.Freeze();
            return decorations;
        }

        private static Paragraph CreateParagraph()
        {
            return new Paragraph
            {
                Margin = new Thickness(0, 1, 0, 1)
            };
        }

        private static int GetLineStartOffset(
            bool isRefactoredSide,
            IReadOnlyList<int>? lineStartOffsets,
            int realLineIndex)
        {
            if (isRefactoredSide ||
                lineStartOffsets == null ||
                realLineIndex >= lineStartOffsets.Count)
            {
                return 0;
            }

            return lineStartOffsets[realLineIndex];
        }

        private void ApplyLineBackground(
            Paragraph paragraph,
            string line,
            string? opposingLine,
            bool isRefactoredSide)
        {
            if (opposingLine == null)
            {
                paragraph.Background = isRefactoredSide ? AdditionBrush : DeletionBrush;
            }
            else if (!_sqlDiffService.IsEquivalentForDiff(line, opposingLine))
            {
                paragraph.Background = ModificationBrush;
            }
        }

        private void AddFormattedSqlLine(
            Paragraph paragraph,
            string text,
            IReadOnlyList<ScalarSubquery>? subqueries,
            int lineStartOffset,
            HashSet<ScalarSubquery>? handledSubqueries,
            Func<ScalarSubquery, UIElement>? quickFixElementFactory)
        {
            if (string.IsNullOrEmpty(text))
            {
                paragraph.Inlines.Add(new Run(""));
                return;
            }

            foreach (SqlDiffToken token in _sqlDiffService.TokenizeLine(text))
            {
                ScalarSubquery? overlappingSubquery = FindOverlappingSubquery(
                    token,
                    subqueries,
                    lineStartOffset);

                AddQuickFixElementIfNeeded(
                    paragraph,
                    overlappingSubquery,
                    handledSubqueries,
                    quickFixElementFactory);

                Run run = CreateRun(token);
                if (overlappingSubquery != null)
                {
                    run.TextDecorations = SquigglyUnderline;
                    run.ToolTip = "Scalar subquery can be optimized to JOIN. Click the lightbulb to apply and compare.";
                }

                paragraph.Inlines.Add(run);
            }
        }

        private static ScalarSubquery? FindOverlappingSubquery(
            SqlDiffToken token,
            IReadOnlyList<ScalarSubquery>? subqueries,
            int lineStartOffset)
        {
            if (subqueries == null)
            {
                return null;
            }

            int tokenAbsoluteStart = lineStartOffset + token.Start;
            int tokenAbsoluteEnd = tokenAbsoluteStart + token.Length;

            foreach (ScalarSubquery subquery in subqueries)
            {
                int subStart = subquery.StartOffset;
                int subEnd = subStart + subquery.FragmentLength;
                if (tokenAbsoluteStart < subEnd && tokenAbsoluteEnd > subStart)
                {
                    return subquery;
                }
            }

            return null;
        }

        private static void AddQuickFixElementIfNeeded(
            Paragraph paragraph,
            ScalarSubquery? subquery,
            HashSet<ScalarSubquery>? handledSubqueries,
            Func<ScalarSubquery, UIElement>? quickFixElementFactory)
        {
            if (subquery == null ||
                handledSubqueries == null ||
                quickFixElementFactory == null ||
                handledSubqueries.Contains(subquery))
            {
                return;
            }

            handledSubqueries.Add(subquery);
            UIElement element = quickFixElementFactory(subquery);
            paragraph.Inlines.Add(new InlineUIContainer(element)
            {
                BaselineAlignment = BaselineAlignment.Center
            });
        }

        private static Run CreateRun(SqlDiffToken token)
        {
            return token.Kind switch
            {
                SqlDiffTokenKind.Comment => new Run(token.Text) { Foreground = Brushes.Green },
                SqlDiffTokenKind.StringLiteral => new Run(token.Text) { Foreground = Brushes.Brown },
                SqlDiffTokenKind.Keyword => new Run(token.Text)
                {
                    Foreground = Brushes.Blue,
                    FontWeight = FontWeights.Bold
                },
                _ => new Run(token.Text) { Foreground = Brushes.Black }
            };
        }
    }
}
