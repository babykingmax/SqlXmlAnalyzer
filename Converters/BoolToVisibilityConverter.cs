using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SqlXmlAnalyzer.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool val = value is bool b && b;
            string param = parameter as string ?? string.Empty;
            bool negate = param.Equals("negate", StringComparison.OrdinalIgnoreCase) || param.Equals("not", StringComparison.OrdinalIgnoreCase);

            if (negate) val = !val;

            return val ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
