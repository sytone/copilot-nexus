namespace CopilotFamily.App.Converters;

using System.Globalization;
using Avalonia.Data.Converters;

/// <summary>
/// Returns true when value is non-null/non-empty, false when null or empty.
/// Bind to IsVisible in Avalonia.
/// </summary>
public class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
            return !string.IsNullOrEmpty(s);

        return value != null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
