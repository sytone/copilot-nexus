namespace CopilotFamily.App.Tests.Converters;

using System.Globalization;
using CopilotFamily.App.Converters;
using CopilotFamily.Core.Models;
using Xunit;

public class MessageRoleToLabelConverterTests
{
    private readonly MessageRoleToLabelConverter _converter = new();

    [Theory]
    [InlineData(MessageRole.User, "You")]
    [InlineData(MessageRole.Assistant, "Copilot")]
    [InlineData(MessageRole.System, "System")]
    public void Convert_KnownRoles_ReturnsExpectedLabel(MessageRole role, string expected)
    {
        var result = _converter.Convert(role, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_NonRoleValue_ReturnsUnknown()
    {
        var result = _converter.Convert("invalid", typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Equal("Unknown", result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(
            () => _converter.ConvertBack("You", typeof(MessageRole), null!, CultureInfo.InvariantCulture));
    }
}
