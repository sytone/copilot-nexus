namespace CopilotNexus.App.Converters;

using System.Globalization;
using Avalonia.Data.Converters;

/// <summary>
/// Returns true when value is true, false when false.
/// Bind to IsVisible in Avalonia.
/// </summary>
public class BoolToVisibleConverter : IValueConverter
{
    public static readonly BoolToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
