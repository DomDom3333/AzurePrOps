using System;
using Avalonia.Data.Converters;

namespace AzurePrOps.Converters;

public class VoteToIconConverter : IValueConverter
{
    public static readonly VoteToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var vote = value as string ?? string.Empty;
        return vote.ToLowerInvariant() switch
        {
            "approved" => "✅",
            "approved with suggestions" => "📝",
            "waiting for author" => "⏳",
            "rejected" => "❌",
            _ => "❔"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
