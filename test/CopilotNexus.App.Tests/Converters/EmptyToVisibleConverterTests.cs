namespace CopilotNexus.App.Tests.Converters;

using System.Globalization;
using CopilotNexus.App.Converters;
using Xunit;

public class EmptyToVisibleConverterTests
{
    private readonly EmptyToVisibleConverter _converter = EmptyToVisibleConverter.Instance;

    [Fact]
    public void Convert_ZeroCount_ReturnsTrue()
    {
        var result = _converter.Convert(0, typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Equal(true, result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void Convert_NonZeroCount_ReturnsFalse(int count)
    {
        var result = _converter.Convert(count, typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_NonIntValue_ReturnsFalse()
    {
        var result = _converter.Convert("not a number", typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(
            () => _converter.ConvertBack(true, typeof(int), null!, CultureInfo.InvariantCulture));
    }
}
