using System;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AzurePrOps.Converters;

public class StatusToBrushConverter : IValueConverter
{
    public static readonly StatusToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var status = value as string ?? string.Empty;
        return status.ToLowerInvariant() switch
        {
            "active" => new SolidColorBrush(Color.Parse("#0078D7")),
            "completed" => new SolidColorBrush(Color.Parse("#107C10")),
            "abandoned" => new SolidColorBrush(Color.Parse("#d13438")),
            _ => new SolidColorBrush(Colors.Gray)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
