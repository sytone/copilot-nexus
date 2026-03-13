namespace CopilotNexus.Core.Tests;

using CopilotNexus.Core.Models;
using Xunit;

public sealed class RuntimeAgentTypeTests
{
    [Theory]
    [InlineData("pi", RuntimeAgentType.Pi)]
    [InlineData("PI", RuntimeAgentType.Pi)]
    [InlineData("copilot-sdk", RuntimeAgentType.CopilotSdk)]
    [InlineData("COPILOT-SDK", RuntimeAgentType.CopilotSdk)]
    public void TryParse_AcceptsSupportedValues(string raw, RuntimeAgentType expected)
    {
        var ok = RuntimeAgentTypeExtensions.TryParse(raw, out var parsed);

        Assert.True(ok);
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void TryParse_ReturnsFalse_ForUnsupportedValues()
    {
        var ok = RuntimeAgentTypeExtensions.TryParse("unknown", out var parsed);

        Assert.False(ok);
        Assert.Equal(RuntimeAgentType.Pi, parsed);
    }

    [Fact]
    public void ToConfigValue_ReturnsExpectedTokens()
    {
        Assert.Equal("pi", RuntimeAgentType.Pi.ToConfigValue());
        Assert.Equal("copilot-sdk", RuntimeAgentType.CopilotSdk.ToConfigValue());
    }
}
