using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AzurePrOps.Converters
{
    public class NumberEqualsZeroConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue == 0;
            }
            else if (value is long longValue)
            {
                return longValue == 0;
            }
            else if (value is double doubleValue)
            {
                return Math.Abs(doubleValue) < double.Epsilon;
            }
            else if (value is float floatValue)
            {
                return Math.Abs(floatValue) < float.Epsilon;
            }
            else if (value is decimal decimalValue)
            {
                return decimalValue == 0;
            }
            else if (value is null)
            {
                return true; // Null is considered as zero/empty
            }

            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
