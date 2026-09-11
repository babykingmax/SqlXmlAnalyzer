using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer
{
    public partial class StatisticsHistogramControl : UserControl
    {
        private string _paramName = "";
        private string _compiledValueStr = "";
        private string _runtimeValueStr = "";
        private bool _compiledValueAvailable;
        private bool _runtimeValueAvailable;
        private double _compiledValue = 0;
        private double _runtimeValue = 0;
        private bool _hasData = false;

        // Loaded real statistics steps
        private List<HistogramStep>? _steps = null;
        private HistogramKeyType _keyType = HistogramKeyType.Numeric;
        private string _dbccCommandsTemplate = "";

        public StatisticsHistogramControl()
        {
            InitializeComponent();
        }

        public void ClearSelection()
        {
            _steps = null; _hasData = false; _dbccCommandsTemplate = "";
            _paramName = ""; _compiledValueStr = ""; _runtimeValueStr = "";
            _compiledValueAvailable = _runtimeValueAvailable = false;
            TxtStatsInput.Clear(); DrawCanvas.Children.Clear(); GridStatsUsage.ItemsSource = null;
            TxtParamName.Text = "未采集参数"; TxtCompiledValue.Text = "N/A"; TxtRuntimeValue.Text = "N/A";
            PanelEstimates.Visibility = Visibility.Collapsed; PanelSniffingRatio.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "未采集当前选择的参数/直方图；不构成健康结论。";
        }

        public void LoadParameterData(string paramName, string compiledValStr, string runtimeValStr)
        {
            // Reset previous manual statistics import
            _steps = null;
            _dbccCommandsTemplate = "";
            TxtStatsInput.Clear();
            PanelEstimates.Visibility = Visibility.Collapsed;
            PanelSniffingRatio.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "当前显示：概念性教学模拟图表";
            TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(21, 101, 192));
            ((Border)TxtStatus.Parent).Background = new SolidColorBrush(Color.FromRgb(227, 242, 253));

            _paramName = paramName;
            _compiledValueAvailable = !string.IsNullOrWhiteSpace(compiledValStr);
            _runtimeValueAvailable = !string.IsNullOrWhiteSpace(runtimeValStr);
            _compiledValueStr = NormalizeParameterValue(compiledValStr);
            _runtimeValueStr = NormalizeParameterValue(runtimeValStr);

            bool parsedCompiled = SqlXmlAnalyzer.Core.NumericParser.TryParseInvariantDouble(_compiledValueStr, out _compiledValue)
                && double.IsFinite(_compiledValue);
            bool parsedRuntime = SqlXmlAnalyzer.Core.NumericParser.TryParseInvariantDouble(_runtimeValueStr, out _runtimeValue)
                && double.IsFinite(_runtimeValue);

            if (!parsedCompiled && !parsedRuntime)
            {
                _compiledValue = Math.Abs(_compiledValueStr.GetHashCode() % 1000);
                _runtimeValue = Math.Abs(_runtimeValueStr.GetHashCode() % 1000);
                if (_compiledValue == _runtimeValue) _runtimeValue += 500;
            }
            else if (!parsedCompiled) _compiledValue = _runtimeValue * 0.1;
            else if (!parsedRuntime)
            {
                double simulatedRuntime = _compiledValue * 10;
                _runtimeValue = double.IsFinite(simulatedRuntime)
                    ? simulatedRuntime : Math.CopySign(double.MaxValue, _compiledValue);
            }

            TxtParamName.Text = _paramName;
            TxtCompiledValue.Text = _compiledValueAvailable ? _compiledValueStr : "N/A";
            TxtRuntimeValue.Text = _runtimeValueAvailable ? _runtimeValueStr : "N/A";

            _hasData = true;

            // Reapply stats if they are already loaded
            if (_steps != null)
            {
                ApplyStatistics();
            }
            else
            {
                RedrawHistogram();
            }
        }

        public void LoadStatisticsUsage(System.Xml.Linq.XDocument doc, System.Xml.Linq.XNamespace ns)
        {
            try
            {
                var stats = SqlXmlAnalyzer.Core.Parsers.StatisticsUsageParser.Parse(doc, ns);
                GridStatsUsage.ItemsSource = stats;

                // Prepopulate DBCC command template for DBA convenience
                if (_steps == null && (string.IsNullOrWhiteSpace(TxtStatsInput.Text) || TxtStatsInput.Text.StartsWith("--")))
                {
                    if (stats != null && stats.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("-- 💡 SqlXmlAnalyzer 推荐的 DBCC SHOW_STATISTICS 查询命令");
                        sb.AppendLine("-- 请复制并在 SSMS 中运行相关命令，然后复制返回的直方图表，覆盖粘贴回此输入框。");
                        sb.AppendLine();

                        foreach (var stat in stats)
                        {
                            try
                            {
                                sb.AppendLine(StatisticsCommandBuilder.BuildShowStatistics(stat));
                            }
                            catch (ArgumentException)
                            {
                                sb.AppendLine("-- 未生成命令：统计对象标识缺失或格式无效，请在 SSMS 中确认对象身份。");
                            }
                        }

                        _dbccCommandsTemplate = sb.ToString();
                    }
                    else
                    {
                        _dbccCommandsTemplate = "-- 💡 未能在执行计划中检测到 OptimizerStatsUsage (统计信息使用情况)。\r\n" +
                                                 "-- 如果你已知晓表名和统计对象，可手动在 SSMS 中执行：\r\n" +
                                                 "-- DBCC SHOW_STATISTICS ('[表名]', '[统计或索引名]');\r\n" +
                                                 "-- 并将第三张网格图（直方图数据）复制粘贴到这里。";
                    }

                    TxtStatsInput.Text = _dbccCommandsTemplate;
                }
            }
            catch (Exception ex)
            {
                SqlXmlAnalyzer.Logger.LogException("LoadStatisticsUsage", ex);
            }
        }

        private void DrawCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_hasData)
            {
                RedrawHistogram();
            }
        }

        private void BtnApplyStats_Click(object sender, RoutedEventArgs e)
        {
            string inputText = TxtStatsInput.Text;
            if (string.IsNullOrWhiteSpace(inputText))
            {
                MessageBox.Show("请先在输入框中粘贴 DBCC SHOW_STATISTICS 的直方图数据！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var parsedSteps = StatisticsHistogramParser.Parse(inputText, out HistogramKeyType detectedType);
                if (parsedSteps == null || parsedSteps.Count == 0)
                {
                    MessageBox.Show("未能识别有效的直方图数据，请检查复制格式是否包含 RANGE_HI_KEY 等表头，或者数据是否完整。", "解析失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _steps = parsedSteps;
                _keyType = detectedType;

                SqlXmlAnalyzer.Logger.Info($"Statistics imported manually: {parsedSteps.Count} steps, KeyType={detectedType}");
                ApplyStatistics();
            }
            catch (Exception ex)
            {
                SqlXmlAnalyzer.Logger.LogException("BtnApplyStats_Click: Failed to parse manually pasted statistics", ex);
                MessageBox.Show($"解析数据出错: {ex.Message}\n请确保复制自 SSMS 直方图网格且以 Tab 分割。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearStats_Click(object sender, RoutedEventArgs e)
        {
            _steps = null;
            TxtStatsInput.Text = _dbccCommandsTemplate;
            PanelEstimates.Visibility = Visibility.Collapsed;
            PanelSniffingRatio.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "当前显示：概念性教学模拟图表";
            TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(21, 101, 192));
            ((Border)TxtStatus.Parent).Background = new SolidColorBrush(Color.FromRgb(227, 242, 253));

            SqlXmlAnalyzer.Logger.Info("Statistics histogram cleared. Restored mock rendering.");
            RedrawHistogram();
        }

        private void ApplyStatistics()
        {
            if (_steps == null || _steps.Count == 0) return;

            // 1. Calculate compiled and runtime estimate details using the core parser
            StatisticsHistogramParser.EstimateValue(_compiledValueAvailable ? _compiledValueStr : null, _steps, _keyType, out double compEst, out _, out string compMatch);
            StatisticsHistogramParser.EstimateValue(_runtimeValueAvailable ? _runtimeValueStr : null, _steps, _keyType, out double runEst, out _, out string runMatch);

            // Update labels
            TxtCompiledEstimate.Text = FormatEstimate(compEst, compMatch);
            TxtRuntimeEstimate.Text = FormatEstimate(runEst, runMatch);

            SqlXmlAnalyzer.Logger.Info($"ApplyStatistics: KeyType={_keyType}, CompiledEstimateAvailable={double.IsFinite(compEst)}, RuntimeEstimateAvailable={double.IsFinite(runEst)}");

            PanelEstimates.Visibility = Visibility.Visible;

            // 2. Show sniffing deviation ratio
            double ratio = 1.0;
            if (double.IsFinite(compEst) && double.IsFinite(runEst) && compEst > 0 && runEst > 0 && double.IsFinite(Math.Max(compEst, runEst) / Math.Min(compEst, runEst)))
            {
                ratio = Math.Max(compEst, runEst) / Math.Min(compEst, runEst);
                TxtSniffingRatio.Text = $"{ratio:F1} 倍";
                TxtSniffingRatio.Foreground = ratio > 100 ? new SolidColorBrush(Color.FromRgb(211, 47, 47)) : new SolidColorBrush(Color.FromRgb(230, 81, 0));
                PanelSniffingRatio.Visibility = Visibility.Visible;
            }
            else
            {
                PanelSniffingRatio.Visibility = Visibility.Collapsed;
            }

            TxtStatus.Text = $"已载入 {_steps.Count} 个直方图区间 (类型: {_keyType})；估算仅用于直方图说明，不能替代 SQL Server 基数估算。";
            TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));
            ((Border)TxtStatus.Parent).Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));

            RedrawHistogram();
        }

        private void RedrawHistogram()
        {
            try
            {
                DrawCanvas.Children.Clear();
                if (!_hasData || DrawCanvas.ActualWidth == 0 || DrawCanvas.ActualHeight == 0) return;

                double width = DrawCanvas.ActualWidth;
                double height = DrawCanvas.ActualHeight;

                if (_steps == null)
                {
                    // FALLBACK: Draw deterministic mock histogram
                    DrawMockHistogram(width, height);
                }
                else
                {
                    // REAL DRAW: Draw based on imported DBCC statistics
                    DrawRealHistogram(width, height);
                }
            }
            catch (Exception ex)
            {
                SqlXmlAnalyzer.Logger.LogException("RedrawHistogram", ex);
            }
        }

        private void DrawMockHistogram(double width, double height)
        {
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0
                || !double.IsFinite(_compiledValue) || !double.IsFinite(_runtimeValue)) return;
            // Mock coordinates are relative. Normalize before subtraction so
            // finite opposite extremes cannot overflow the synthetic range.
            double coordinateScale = Math.Max(Math.Abs(_compiledValue), Math.Abs(_runtimeValue));
            if (coordinateScale == 0) coordinateScale = 1;
            double compiledValue = _compiledValue / coordinateScale;
            double runtimeValue = _runtimeValue / coordinateScale;
            int stepsCount = 20;
            double[] stepValues = new double[stepsCount];
            double maxVal = Math.Max(compiledValue, runtimeValue);
            double minVal = Math.Min(compiledValue, runtimeValue);
            double range = maxVal - minVal;
            if (range == 0) range = 1;

            double startXValue = minVal - range * 0.2;
            double endXValue = maxVal + range * 0.2;
            double stepSize = (endXValue - startXValue) / stepsCount;

            Random rnd = new Random(42);
            double maxEqRows = 0;

            for (int i = 0; i < stepsCount; i++)
            {
                double xVal = startXValue + i * stepSize;
                double dist1 = Math.Exp(-Math.Pow((xVal - compiledValue) / (range * 0.1), 2));
                double dist2 = Math.Exp(-Math.Pow((xVal - runtimeValue) / (range * 0.3), 2));
                double eqRows = (dist1 * 10000) + (dist2 * 500) + rnd.Next(50, 200);

                stepValues[i] = eqRows;
                if (eqRows > maxEqRows) maxEqRows = eqRows;
            }

            // Draw grid lines
            for (int i = 0; i <= 4; i++)
            {
                double y = height - (i * height / 4.0);
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection(new[] { 4.0, 4.0 })
                };
                DrawCanvas.Children.Add(line);

                var txt = new TextBlock
                {
                    Text = (maxEqRows * i / 4.0).ToString("N0"),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(158, 158, 158))
                };
                Canvas.SetLeft(txt, -28);
                Canvas.SetTop(txt, y - 6);
                DrawCanvas.Children.Add(txt);
            }

            // Draw bars
            double barWidth = width / stepsCount;
            for (int i = 0; i < stepsCount; i++)
            {
                double barHeight = (stepValues[i] / maxEqRows) * height;
                var rect = new Rectangle
                {
                    Width = Math.Max(0, barWidth - 2),
                    Height = barHeight,
                    Fill = new SolidColorBrush(Color.FromRgb(144, 202, 249)),
                    Stroke = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    StrokeThickness = 1,
                    Opacity = 0.8
                };
                Canvas.SetLeft(rect, i * barWidth + 1);
                Canvas.SetTop(rect, height - barHeight);
                DrawCanvas.Children.Add(rect);
            }

            double ValueToX(double val) => ((val - startXValue) / (endXValue - startXValue)) * width;

            double compiledX = ValueToX(compiledValue);
            double runtimeX = ValueToX(runtimeValue);

            DrawVerticalLine(compiledX, height, Color.FromRgb(25, 118, 210), "Compiled");
            DrawVerticalLine(runtimeX, height, Color.FromRgb(211, 47, 47), "Runtime");
        }

        private void DrawRealHistogram(double width, double height)
        {
            if (_steps == null || _steps.Count == 0) return;

            // 1. Convert parameter values to numeric positions
            StatisticsHistogramParser.EstimateValue(_compiledValueAvailable ? _compiledValueStr : null, _steps, _keyType, out double compEst, out double compNumPos, out _);
            StatisticsHistogramParser.EstimateValue(_runtimeValueAvailable ? _runtimeValueStr : null, _steps, _keyType, out double runEst, out double runNumPos, out _);

            // Normalize drawing coordinates before computing the span to avoid overflow.
            var plottedSteps = _steps.Where(s => double.IsFinite(s.RangeHiKeyNumeric) && double.IsFinite(s.EqRows)).ToList();
            if (plottedSteps.Count == 0) return;
            double minStepKey = plottedSteps.Min(s => s.RangeHiKeyNumeric);
            double maxStepKey = plottedSteps.Max(s => s.RangeHiKeyNumeric);
            double coordinateScale = Math.Max(1, Math.Max(Math.Abs(minStepKey), Math.Abs(maxStepKey)));
            double startXValue = minStepKey / coordinateScale;
            double endXValue = maxStepKey / coordinateScale;
            double range = endXValue - startXValue;
            if (range == 0) range = 1.0;

            // Padding range slightly
            startXValue -= range * 0.05;
            endXValue += range * 0.05;
            range = endXValue - startXValue;

            // Y scale based on Max(EQ_ROWS, AVG_RANGE_ROWS)
            double maxRowsVal = plottedSteps.Max(s => Math.Max(s.EqRows, double.IsFinite(s.AvgRangeRows) ? s.AvgRangeRows : 0));
            if (maxRowsVal == 0) maxRowsVal = 10.0;

            double ValueToX(double val) => ((val / coordinateScale - startXValue) / range) * width;

            // Draw Y-axis grids
            for (int i = 0; i <= 4; i++)
            {
                double y = height - (i * height / 4.0);
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection(new[] { 4.0, 4.0 })
                };
                DrawCanvas.Children.Add(line);

                var txt = new TextBlock
                {
                    Text = (maxRowsVal * (i / 4.0)).ToString("N0"),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(158, 158, 158))
                };
                Canvas.SetLeft(txt, -28);
                Canvas.SetTop(txt, y - 6);
                DrawCanvas.Children.Add(txt);
            }

            // Draw Range Block Bars and EQ_ROWS Pins
            for (int i = 0; i < plottedSteps.Count; i++)
            {
                var step = plottedSteps[i];
                double xCurr = ValueToX(step.RangeHiKeyNumeric);

                // 1. Draw Range average bar between step[i-1] and step[i]
                if (i > 0 && double.IsFinite(step.AvgRangeRows))
                {
                    double xPrev = ValueToX(plottedSteps[i - 1].RangeHiKeyNumeric);
                    double barW = xCurr - xPrev;
                    if (barW > 0.5)
                    {
                        double barH = (step.AvgRangeRows / maxRowsVal) * height;
                        var rect = new Rectangle
                        {
                            Width = barW,
                            Height = Math.Max(2, barH),
                            Fill = new SolidColorBrush(Color.FromArgb(50, 33, 150, 243)), // Translucent blue
                            Stroke = new SolidColorBrush(Color.FromArgb(80, 33, 150, 243)),
                            StrokeThickness = 0.5,
                            ToolTip = $"区间: {plottedSteps[i - 1].RangeHiKey} ~ {step.RangeHiKey}\n区间内行数 (RANGE_ROWS): {step.RangeRows:N0}\n区间内均值 (AVG_RANGE_ROWS): {step.AvgRangeRows:N1}"
                        };
                        Canvas.SetLeft(rect, xPrev);
                        Canvas.SetTop(rect, height - barH);
                        DrawCanvas.Children.Add(rect);
                    }
                }

                // 2. Draw EQ_ROWS Pin/Line at xCurr
                double pinH = (step.EqRows / maxRowsVal) * height;
                var pin = new Line
                {
                    X1 = xCurr,
                    Y1 = height - pinH,
                    X2 = xCurr,
                    Y2 = height,
                    Stroke = new SolidColorBrush(Color.FromRgb(21, 101, 192)),
                    StrokeThickness = 2.5,
                    ToolTip = $"上限值 (RANGE_HI_KEY): {step.RangeHiKey}\n等值行数 (EQ_ROWS): {step.EqRows:N0}"
                };
                DrawCanvas.Children.Add(pin);

                // Draw a small dot on top of the EQ_ROWS pin
                var dot = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = new SolidColorBrush(Color.FromRgb(13, 71, 161)),
                    ToolTip = pin.ToolTip
                };
                Canvas.SetLeft(dot, xCurr - 2.5);
                Canvas.SetTop(dot, height - pinH - 2.5);
                DrawCanvas.Children.Add(dot);
            }

            // Draw Parameter indicator vertical lines
            double compiledX = ValueToX(compNumPos);
            double runtimeX = ValueToX(runNumPos);

            if (double.IsFinite(compiledX)) DrawVerticalLine(compiledX, height, Color.FromRgb(25, 118, 210), $"Compiled: {_compiledValueStr} ({compEst:N0}行)");
            if (double.IsFinite(runtimeX)) DrawVerticalLine(runtimeX, height, Color.FromRgb(211, 47, 47), $"Runtime: {_runtimeValueStr} ({runEst:N0}行)");
        }

        private static string FormatEstimate(double rows, string match) => double.IsFinite(rows)
            ? $"估算返回: {rows:N2} 行 ({match})" : $"估算返回: N/A ({match})";

        private static string NormalizeParameterValue(string value)
        {
            value = value.Trim();
            while (value.Length >= 2 && value[0] == '(' && value[^1] == ')') value = value[1..^1].Trim();
            if (value.StartsWith("N'", StringComparison.OrdinalIgnoreCase)) value = value[1..];
            return value.Length >= 2 && value[0] == '\'' && value[^1] == '\''
                ? value[1..^1].Replace("''", "'") : value;
        }

        private void DrawVerticalLine(double x, double height, Color color, string label)
        {
            if (x < 0) x = 0;
            if (x > DrawCanvas.ActualWidth) x = DrawCanvas.ActualWidth;

            var brush = new SolidColorBrush(color);

            var line = new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = height,
                Stroke = brush,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 })
            };
            DrawCanvas.Children.Add(line);

            var txt = new TextBlock
            {
                Text = label,
                Foreground = brush,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Background = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                Padding = new Thickness(4, 2, 4, 2)
            };

            // Adjust label offset so it doesn't clip off screen
            if (x > DrawCanvas.ActualWidth - 120)
            {
                Canvas.SetLeft(txt, x - 120);
            }
            else
            {
                Canvas.SetLeft(txt, x + 4);
            }
            Canvas.SetTop(txt, 4);
            DrawCanvas.Children.Add(txt);
        }
    }
}
