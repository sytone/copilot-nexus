namespace CopilotNexus.App.Converters;

using System.Globalization;
using Avalonia.Data.Converters;

/// <summary>
/// Returns false when value is true, true when false.
/// Bind to IsVisible in Avalonia.
/// </summary>
public class InverseBoolToVisibleConverter : IValueConverter
{
    public static readonly InverseBoolToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
