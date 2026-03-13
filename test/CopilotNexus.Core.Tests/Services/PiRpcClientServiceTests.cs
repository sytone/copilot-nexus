namespace CopilotNexus.Core.Tests.Services;

using CopilotNexus.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class PiRpcClientServiceTests
{
    private static readonly SemaphoreSlim EnvLock = new(1, 1);

    [Fact]
    public async Task StartAsync_ThrowsActionableError_WhenPiExecutableMissing()
    {
        await EnvLock.WaitAsync();
        var previous = Environment.GetEnvironmentVariable("NEXUS_PI_EXECUTABLE");
        try
        {
            Environment.SetEnvironmentVariable("NEXUS_PI_EXECUTABLE", "pi-missing-for-test");
            await using var service = new PiRpcClientService(NullLogger<PiRpcClientService>.Instance);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync());
            Assert.Contains("Failed to start Pi executable", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXUS_PI_EXECUTABLE", previous);
            EnvLock.Release();
        }
    }

    [Fact]
    public async Task StartAsync_Succeeds_WhenExecutableCanRespond()
    {
        await EnvLock.WaitAsync();
        var previous = Environment.GetEnvironmentVariable("NEXUS_PI_EXECUTABLE");
        try
        {
            Environment.SetEnvironmentVariable("NEXUS_PI_EXECUTABLE", "pwsh");
            await using var service = new PiRpcClientService(NullLogger<PiRpcClientService>.Instance);

            await service.StartAsync();

            Assert.True(service.IsConnected);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXUS_PI_EXECUTABLE", previous);
            EnvLock.Release();
        }
    }
}
