using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SqlXmlAnalyzer
{
    public partial class QuickFixWindow : Window
    {
        public bool Applied { get; private set; } = false;

        private readonly string _originalSql;
        private readonly string _selectedRewriteSql;
        private readonly Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery _subquery;

        private static readonly HashSet<string> SqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "ON", "GROUP", "BY", "ORDER",
            "HAVING", "AND", "OR", "NOT", "IN", "EXISTS", "LIKE", "AS", "CREATE", "INDEX", "DROP", "TABLE",
            "INSERT", "UPDATE", "DELETE", "INTO", "VALUES", "SET", "EXEC", "PROCEDURE", "DECLARE", "WITH",
            "UNION", "ALL", "CASE", "WHEN", "THEN", "ELSE", "END", "NULL", "IS", "CAST", "CONVERT", "GO",
            "CROSS", "APPLY", "TOP", "DISTINCT"
        };

        private static readonly Regex SqlTokenizerRegex =
            new Regex(
                @"(--.*)|('[^']*(?:''[^']*)*')|([a-zA-Z_#@][a-zA-Z0-9_]*)|(\s+)|(.)",
                RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(100));

        public QuickFixWindow(string originalSql, string selectedRewriteSql, Microsoft.SqlServer.TransactSql.ScriptDom.ScalarSubquery subquery)
        {
            InitializeComponent();
            _originalSql = originalSql;
            _selectedRewriteSql = selectedRewriteSql;
            _subquery = subquery;

            RenderOriginalSql();
            RenderOptimizedSql();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Applied = false;
            this.Close();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            Applied = true;
            this.Close();
        }

        private void RenderOriginalSql()
        {
            OriginalSqlTextBox.Document.Blocks.Clear();
            OriginalSqlTextBox.BeginChange();
            try
            {
                var p = new Paragraph { Margin = new Thickness(0) };
                var matches = SqlTokenizerRegex.Matches(_originalSql);

                int subStart = _subquery.StartOffset;
                int subEnd = subStart + _subquery.FragmentLength;

                // Create a squiggly underline brush / decoration
                var squiggly = CreateSquigglyUnderline();

                foreach (Match match in matches)
                {
                    int tokenStart = match.Index;
                    int tokenEnd = match.Index + match.Length;

                    bool isInsideSubquery = (tokenStart < subEnd && tokenEnd > subStart);

                    Run run = new Run(match.Value);
                    if (match.Groups[1].Success) // Comment
                    {
                        run.Foreground = Brushes.Green;
                    }
                    else if (match.Groups[2].Success) // String
                    {
                        run.Foreground = Brushes.Brown;
                    }
                    else if (match.Groups[3].Success) // Word
                    {
                        if (SqlKeywords.Contains(match.Value))
                        {
                            run.Foreground = Brushes.Blue;
                            run.FontWeight = FontWeights.Bold;
                        }
                        else
                        {
                            run.Foreground = Brushes.Black;
                        }
                    }
                    else
                    {
                        run.Foreground = Brushes.Black;
                    }

                    if (isInsideSubquery)
                    {
                        run.TextDecorations = squiggly;
                        // Give it a light background to stand out
                        run.Background = new SolidColorBrush(Color.FromArgb(30, 255, 0, 0)); // Very soft red
                        run.ToolTip = "此处的标量子查询可优化为 JOIN 语句";
                    }

                    p.Inlines.Add(run);
                }
                OriginalSqlTextBox.Document.Blocks.Add(p);
            }
            finally
            {
                OriginalSqlTextBox.EndChange();
            }
        }

        private void RenderOptimizedSql()
        {
            OptimizedSqlTextBox.Document.Blocks.Clear();
            OptimizedSqlTextBox.BeginChange();
            try
            {
                var p = new Paragraph { Margin = new Thickness(0) };
                var matches = SqlTokenizerRegex.Matches(_selectedRewriteSql);

                foreach (Match match in matches)
                {
                    string value = match.Value;
                    Run run = new Run(value);

                    if (match.Groups[1].Success) // Comment
                    {
                        run.Foreground = Brushes.Green;
                    }
                    else if (match.Groups[2].Success) // String
                    {
                        run.Foreground = Brushes.Brown;
                    }
                    else if (match.Groups[3].Success) // Word
                    {
                        if (SqlKeywords.Contains(value))
                        {
                            run.Foreground = Brushes.Blue;
                            run.FontWeight = FontWeights.Bold;
                        }
                        else
                        {
                            run.Foreground = Brushes.Black;
                        }

                        // Highlight the generated join and subquery aliases to make the comparison clear
                        if (value.Contains("t_sub_", StringComparison.OrdinalIgnoreCase) ||
                            value.Contains("agg_", StringComparison.OrdinalIgnoreCase))
                        {
                            run.Background = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0)); // Very soft green
                            run.Foreground = Brushes.DarkGreen;
                            run.FontWeight = FontWeights.SemiBold;
                        }
                    }
                    else
                    {
                        run.Foreground = Brushes.Black;
                    }

                    p.Inlines.Add(run);
                }
                OptimizedSqlTextBox.Document.Blocks.Add(p);
            }
            finally
            {
                OptimizedSqlTextBox.EndChange();
            }
        }

        private static TextDecorationCollection CreateSquigglyUnderline()
        {
            var brush = new DrawingBrush();
            brush.Viewport = new Rect(0, 0, 6, 4);
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.TileMode = TileMode.Tile;

            var geometry = new GeometryGroup();
            var path = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(0, 2) };
            figure.Segments.Add(new BezierSegment(new Point(1.5, 0), new Point(1.5, 4), new Point(3, 2), true));
            figure.Segments.Add(new BezierSegment(new Point(4.5, 0), new Point(4.5, 4), new Point(6, 2), true));
            path.Figures.Add(figure);

            var drawing = new GeometryDrawing(null, new Pen(Brushes.Red, 1.2), path);
            brush.Drawing = drawing;

            var dec = new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Pen = new Pen(brush, 3)
            };

            var decs = new TextDecorationCollection();
            decs.Add(dec);
            return decs;
        }
    }
}
