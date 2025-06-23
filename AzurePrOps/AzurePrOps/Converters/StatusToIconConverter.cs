using System;
using Avalonia.Data.Converters;

namespace AzurePrOps.Converters;

public class StatusToIconConverter : IValueConverter
{
    public static readonly StatusToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var status = value as string ?? string.Empty;
        return status.ToLowerInvariant() switch
        {
            "active" => "🔄",
            "completed" => "✅",
            "abandoned" => "🚫",
            _ => "❔"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
