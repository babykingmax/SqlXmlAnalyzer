using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SqlXmlAnalyzer
{
    public partial class StatisticsHistogramControl : UserControl
    {
        private string _paramName = "";
        private double _compiledValue = 0;
        private double _runtimeValue = 0;
        private bool _hasData = false;

        public StatisticsHistogramControl()
        {
            InitializeComponent();
        }

        public void LoadParameterData(string paramName, string compiledValStr, string runtimeValStr)
        {
            _paramName = paramName;
            
            // Clean values (e.g., if they are wrapped in quotes or parentheses)
            compiledValStr = compiledValStr.Trim('\'', '(', ')');
            runtimeValStr = runtimeValStr.Trim('\'', '(', ')');

            bool parsedCompiled = double.TryParse(compiledValStr, out _compiledValue);
            bool parsedRuntime = double.TryParse(runtimeValStr, out _runtimeValue);

            if (!parsedCompiled && !parsedRuntime)
            {
                // If they are purely strings or dates, we hash them or assign arbitrary spread to visualize
                _compiledValue = Math.Abs(compiledValStr.GetHashCode() % 1000);
                _runtimeValue = Math.Abs(runtimeValStr.GetHashCode() % 1000);
                if (_compiledValue == _runtimeValue) _runtimeValue += 500;
            }
            else if (!parsedCompiled) _compiledValue = _runtimeValue * 0.1;
            else if (!parsedRuntime) _runtimeValue = _compiledValue * 10;

            TxtParamName.Text = _paramName;
            TxtCompiledValue.Text = compiledValStr;
            TxtRuntimeValue.Text = runtimeValStr;

            _hasData = true;
            RedrawHistogram();
        }

        private void DrawCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_hasData)
            {
                RedrawHistogram();
            }
        }

        private void RedrawHistogram()
        {
            DrawCanvas.Children.Clear();
            if (!_hasData || DrawCanvas.ActualWidth == 0 || DrawCanvas.ActualHeight == 0) return;

            double width = DrawCanvas.ActualWidth;
            double height = DrawCanvas.ActualHeight;

            // Generate mock histogram data (20 steps)
            // We create a skewed distribution
            int stepsCount = 20;
            double[] stepValues = new double[stepsCount];
            double maxVal = Math.Max(_compiledValue, _runtimeValue);
            double minVal = Math.Min(_compiledValue, _runtimeValue);
            double range = maxVal - minVal;
            if (range == 0) range = 100;

            double startXValue = minVal - range * 0.2;
            double endXValue = maxVal + range * 0.2;
            double stepSize = (endXValue - startXValue) / stepsCount;

            Random rnd = new Random(42); // deterministic mock
            double maxEqRows = 0;

            for (int i = 0; i < stepsCount; i++)
            {
                double xVal = startXValue + i * stepSize;
                // create a bimodal or skewed distribution
                double dist1 = Math.Exp(-Math.Pow((xVal - _compiledValue) / (range * 0.1), 2));
                double dist2 = Math.Exp(-Math.Pow((xVal - _runtimeValue) / (range * 0.3), 2));
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
                    X1 = 0, Y1 = y, X2 = width, Y2 = y,
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
                    Width = barWidth - 2,
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

            // Function to map value to X coordinate
            double ValueToX(double val)
            {
                return ((val - startXValue) / (endXValue - startXValue)) * width;
            }

            double compiledX = ValueToX(_compiledValue);
            double runtimeX = ValueToX(_runtimeValue);

            // Draw Compiled Line
            DrawVerticalLine(compiledX, height, Color.FromRgb(25, 118, 210), "Compiled");

            // Draw Runtime Line
            DrawVerticalLine(runtimeX, height, Color.FromRgb(211, 47, 47), "Runtime");
        }

        private void DrawVerticalLine(double x, double height, Color color, string label)
        {
            if (x < 0) x = 0;
            if (x > DrawCanvas.ActualWidth) x = DrawCanvas.ActualWidth;

            var brush = new SolidColorBrush(color);

            var line = new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = height,
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
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Padding = new Thickness(2)
            };
            Canvas.SetLeft(txt, x + 4);
            Canvas.SetTop(txt, 4);
            DrawCanvas.Children.Add(txt);
        }
    }
}
