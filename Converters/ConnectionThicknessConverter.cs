using System;
using System.Globalization;
using System.Windows.Data;

namespace SqlXmlAnalyzer
{
    public class ConnectionThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Core.Services.PlanGraphMetricService.CalculateLegacyConverterThickness(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
