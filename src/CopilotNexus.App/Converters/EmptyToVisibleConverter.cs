namespace CopilotNexus.App.Converters;

using System.Globalization;
using Avalonia.Data.Converters;

/// <summary>
/// Converts a count of 0 to true (visible), and any non-zero count to false (hidden).
/// Used to show the welcome screen when no tabs are open.
/// </summary>
public class EmptyToVisibleConverter : IValueConverter
{
    public static readonly EmptyToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
