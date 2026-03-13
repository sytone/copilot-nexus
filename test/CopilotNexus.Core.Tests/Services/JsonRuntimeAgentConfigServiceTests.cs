namespace CopilotNexus.Core.Tests.Services;

using CopilotNexus.Core.Models;
using CopilotNexus.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class JsonRuntimeAgentConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configFile;
    private readonly JsonRuntimeAgentConfigService _service;

    public JsonRuntimeAgentConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CopilotNexusRuntimeConfig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configFile = Path.Combine(_tempDir, "runtime-config.json");
        _service = new JsonRuntimeAgentConfigService(
            NullLogger<JsonRuntimeAgentConfigService>.Instance,
            _configFile);
    }

    [Fact]
    public async Task GetAsync_DefaultsToPi_WhenFileIsMissing()
    {
        var runtime = await _service.GetAsync();

        Assert.Equal(RuntimeAgentType.Pi, runtime);
    }

    [Fact]
    public async Task SetAsync_PersistsRoundTripValue()
    {
        await _service.SetAsync(RuntimeAgentType.CopilotSdk);

        var runtime = await _service.GetAsync();

        Assert.Equal(RuntimeAgentType.CopilotSdk, runtime);
    }

    [Fact]
    public async Task GetAsync_DefaultsToPi_WhenJsonIsInvalid()
    {
        await File.WriteAllTextAsync(_configFile, "{ this is not valid json");

        var runtime = await _service.GetAsync();

        Assert.Equal(RuntimeAgentType.Pi, runtime);
    }

    [Fact]
    public async Task GetAsync_DefaultsToPi_WhenAgentValueIsUnknown()
    {
        await File.WriteAllTextAsync(_configFile, "{ \"agent\": \"unknown\" }");

        var runtime = await _service.GetAsync();

        Assert.Equal(RuntimeAgentType.Pi, runtime);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
