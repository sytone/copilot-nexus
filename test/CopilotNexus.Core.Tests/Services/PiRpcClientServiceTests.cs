namespace CopilotNexus.Core.Tests.Services;

using System.Text;
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

    [Fact]
    public async Task ListModelsAsync_ReturnsModelsFromRpcCatalog()
    {
        await EnvLock.WaitAsync();
        var previous = Environment.GetEnvironmentVariable("NEXUS_PI_EXECUTABLE");
        var tempRoot = Path.Combine(Path.GetTempPath(), $"nexus-pi-rpc-models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var scriptPath = Path.Combine(tempRoot, "pi-test.cmd");
        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                @echo off
                setlocal
                if "%1"=="--version" (
                  echo pi-test 1.0.0
                  exit /b 0
                )
                if "%1"=="--mode" (
                  set /p cmd=
                  echo {"id":"nexus-model-discovery","type":"response","command":"get_available_models","success":true,"data":{"models":[{"id":"claude-sonnet-4-5-20250929","name":"Claude Sonnet 4.5","provider":"anthropic","reasoning":true,"cost":{"input":3.0,"output":15.0}}]}}
                  exit /b 0
                )
                echo unsupported invocation %*
                exit /b 1
                """, Encoding.ASCII);

            Environment.SetEnvironmentVariable("NEXUS_PI_EXECUTABLE", scriptPath);
            await using var service = new PiRpcClientService(NullLogger<PiRpcClientService>.Instance);

            await service.StartAsync();
            var models = await service.ListModelsAsync();

            var model = Assert.Single(models);
            Assert.Equal("anthropic/claude-sonnet-4-5-20250929", model.ModelId);
            Assert.Equal("Claude Sonnet 4.5 (anthropic)", model.Name);
            Assert.Contains("reasoning", model.Capabilities);
            Assert.Contains(model.Capabilities, capability => capability.StartsWith("cost:", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXUS_PI_EXECUTABLE", previous);
            Directory.Delete(tempRoot, recursive: true);
            EnvLock.Release();
        }
    }

    [Fact]
    public async Task ListModelsAsync_FallsBackWhenRpcDiscoveryFails()
    {
        await EnvLock.WaitAsync();
        var previous = Environment.GetEnvironmentVariable("NEXUS_PI_EXECUTABLE");
        var tempRoot = Path.Combine(Path.GetTempPath(), $"nexus-pi-rpc-models-fallback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var scriptPath = Path.Combine(tempRoot, "pi-test.cmd");
        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                @echo off
                setlocal
                if "%1"=="--version" (
                  echo pi-test 1.0.0
                  exit /b 0
                )
                if "%1"=="--mode" (
                  set /p cmd=
                  echo {"id":"nexus-model-discovery","type":"response","command":"get_available_models","success":false,"error":"auth required"}
                  exit /b 0
                )
                echo unsupported invocation %*
                exit /b 1
                """, Encoding.ASCII);

            Environment.SetEnvironmentVariable("NEXUS_PI_EXECUTABLE", scriptPath);
            await using var service = new PiRpcClientService(NullLogger<PiRpcClientService>.Instance);

            await service.StartAsync();
            var models = await service.ListModelsAsync();

            var model = Assert.Single(models);
            Assert.Equal("pi-auto", model.ModelId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXUS_PI_EXECUTABLE", previous);
            Directory.Delete(tempRoot, recursive: true);
            EnvLock.Release();
        }
    }
}
