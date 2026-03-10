namespace CopilotFamily.App.Converters;

using System.Globalization;
using Avalonia.Data.Converters;
using CopilotFamily.Core.Models;

public class MessageRoleToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageRole role)
        {
            return role switch
            {
                MessageRole.User => "You",
                MessageRole.Assistant => "Copilot",
                MessageRole.System => "System",
                _ => "Unknown"
            };
        }

        return "Unknown";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
