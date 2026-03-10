namespace CopilotFamily.App.Converters;

using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CopilotFamily.Core.Models;

public class MessageRoleToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush UserBrush = new(Color.FromRgb(86, 156, 214));
    private static readonly SolidColorBrush AssistantBrush = new(Color.FromRgb(204, 204, 204));
    private static readonly SolidColorBrush SystemBrush = new(Color.FromRgb(128, 128, 128));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageRole role)
        {
            return role switch
            {
                MessageRole.User => UserBrush,
                MessageRole.Assistant => AssistantBrush,
                MessageRole.System => SystemBrush,
                _ => AssistantBrush
            };
        }

        return AssistantBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
