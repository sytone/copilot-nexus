namespace CopilotNexus.App.Tests.Converters;

using System.Globalization;
using Avalonia.Media;
using CopilotNexus.App.Converters;
using CopilotNexus.Core.Models;
using Xunit;

public class MessageRoleToBrushConverterTests
{
    private readonly MessageRoleToBrushConverter _converter = new();

    [Fact]
    public void Convert_UserRole_ReturnsBlueBrush()
    {
        var result = _converter.Convert(MessageRole.User, typeof(IBrush), null!, CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.FromRgb(86, 156, 214), brush.Color);
    }

    [Fact]
    public void Convert_AssistantRole_ReturnsLightGrayBrush()
    {
        var result = _converter.Convert(MessageRole.Assistant, typeof(IBrush), null!, CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.FromRgb(204, 204, 204), brush.Color);
    }

    [Fact]
    public void Convert_SystemRole_ReturnsGrayBrush()
    {
        var result = _converter.Convert(MessageRole.System, typeof(IBrush), null!, CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.FromRgb(128, 128, 128), brush.Color);
    }

    [Fact]
    public void Convert_NonRoleValue_ReturnsAssistantBrush()
    {
        var result = _converter.Convert("invalid", typeof(IBrush), null!, CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.FromRgb(204, 204, 204), brush.Color);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(
            () => _converter.ConvertBack(null, typeof(MessageRole), null!, CultureInfo.InvariantCulture));
    }
}
