using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SqlXmlAnalyzer.Converters
{
    public class StepToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0]: Item's StepNumber (int)
            // values[1]: CurrentStep (int)
            // values[2]: IsInCycle (bool)
            // values[3]: FocusCriticalPath (bool)

            if (values.Length < 4) return Visibility.Visible;

            if (values[0] is int itemStep && values[1] is int currentStep &&
                values[2] is bool isInCycle && values[3] is bool focusCritical)
            {
                // If focus critical path is ON, and item is NOT in cycle, collapse it
                if (focusCritical && !isInCycle) return Visibility.Collapsed;

                // Step logic: if item step <= current step, it has "happened"
                return itemStep <= currentStep ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    public class StepToOpacityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // For future edges: dim them instead of completely hiding
            if (values.Length < 4) return 1.0;

            if (values[0] is int itemStep && values[1] is int currentStep &&
                values[2] is bool isInCycle && values[3] is bool focusCritical)
            {
                if (focusCritical && !isInCycle) return 0.0;
                return itemStep <= currentStep ? 1.0 : 0.2;
            }

            return 1.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
