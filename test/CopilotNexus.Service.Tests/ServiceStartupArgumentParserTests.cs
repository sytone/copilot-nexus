namespace CopilotNexus.Service.Tests;

using CopilotNexus.Core.Models;
using CopilotNexus.Service;
using Xunit;

public sealed class ServiceStartupArgumentParserTests
{
    [Fact]
    public void Parse_LeavesArgsUntouched_WhenNoAgentProvided()
    {
        var parsed = ServiceStartupArgumentParser.Parse(["--urls", "http://localhost:5280"]);

        Assert.Null(parsed.Error);
        Assert.Null(parsed.AgentOverride);
        Assert.Equal(["--urls", "http://localhost:5280"], parsed.ForwardedArgs);
    }

    [Fact]
    public void Parse_AcceptsSeparatedAgentValue()
    {
        var parsed = ServiceStartupArgumentParser.Parse(["--agent", "copilot-sdk", "--urls", "http://localhost:5280"]);

        Assert.Null(parsed.Error);
        Assert.Equal(RuntimeAgentType.CopilotSdk, parsed.AgentOverride);
        Assert.Equal(["--urls", "http://localhost:5280"], parsed.ForwardedArgs);
    }

    [Fact]
    public void Parse_AcceptsEqualsAgentValue()
    {
        var parsed = ServiceStartupArgumentParser.Parse(["--agent=pi", "--urls", "http://localhost:5280"]);

        Assert.Null(parsed.Error);
        Assert.Equal(RuntimeAgentType.Pi, parsed.AgentOverride);
        Assert.Equal(["--urls", "http://localhost:5280"], parsed.ForwardedArgs);
    }

    [Fact]
    public void Parse_ReturnsError_WhenAgentValueMissing()
    {
        var parsed = ServiceStartupArgumentParser.Parse(["--agent"]);

        Assert.NotNull(parsed.Error);
        Assert.Contains("Missing value for --agent", parsed.Error);
    }

    [Fact]
    public void Parse_ReturnsError_WhenAgentValueInvalid()
    {
        var parsed = ServiceStartupArgumentParser.Parse(["--agent", "unknown"]);

        Assert.NotNull(parsed.Error);
        Assert.Contains("Invalid --agent", parsed.Error);
    }
}
