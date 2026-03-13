namespace CopilotNexus.Service.Tests;

using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using CopilotNexus.Core.Services;
using CopilotNexus.Service;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class NexusHostBuilderRuntimeTests
{
    [Fact]
    public void CreateBuilder_RegistersPiRuntime_WhenPiSelected()
    {
        var originalTestMode = Environment.GetEnvironmentVariable("NEXUS_TEST_MODE");
        Environment.SetEnvironmentVariable("NEXUS_TEST_MODE", "1");
        try
        {
            var builder = NexusHostBuilder.CreateBuilder(RuntimeAgentType.Pi, []);
            using var app = builder.Build();

            var runtimeClient = app.Services.GetRequiredService<IAgentClientService>();

            Assert.IsType<PiRpcClientService>(runtimeClient);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXUS_TEST_MODE", originalTestMode);
        }
    }

    [Fact]
    public void CreateBuilder_RegistersCopilotSdkRuntime_WhenCopilotSdkSelected()
    {
        var originalTestMode = Environment.GetEnvironmentVariable("NEXUS_TEST_MODE");
        Environment.SetEnvironmentVariable("NEXUS_TEST_MODE", "1");
        try
        {
            var builder = NexusHostBuilder.CreateBuilder(RuntimeAgentType.CopilotSdk, []);
            using var app = builder.Build();

            var runtimeClient = app.Services.GetRequiredService<IAgentClientService>();

            Assert.IsType<CopilotClientService>(runtimeClient);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXUS_TEST_MODE", originalTestMode);
        }
    }
}
