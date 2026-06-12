using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SqlXmlAnalyzer.Converters
{
    public class ScoreToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int score)
            {
                if (score >= 90) return new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
                if (score >= 70) return new SolidColorBrush(Color.FromRgb(234, 179, 8)); // Yellow
                return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
