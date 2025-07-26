using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AzurePrOps.Converters;

public class CollectionNotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasItems = false;
        if (value is IEnumerable enumerable)
        {
            foreach (var _ in enumerable)
            {
                hasItems = true;
                break;
            }
        }

        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
            return !hasItems;

        return hasItems;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
