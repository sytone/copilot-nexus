namespace CopilotNexus.Core.Tests.Services;

using CopilotNexus.Core.Models;
using CopilotNexus.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class JsonSessionProfileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _profilesFile;
    private readonly JsonSessionProfileService _service;

    public JsonSessionProfileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CopilotNexusProfiles_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _profilesFile = Path.Combine(_tempDir, "session-profiles.json");
        _service = new JsonSessionProfileService(
            NullLogger<JsonSessionProfileService>.Instance,
            _profilesFile);
    }

    [Fact]
    public async Task ListAsync_ReturnsDefaultProfileWhenEmpty()
    {
        var profiles = await _service.ListAsync();

        Assert.NotEmpty(profiles);
        Assert.Contains(profiles, p => p.Id == "default");
    }

    [Fact]
    public async Task SaveAsync_PersistsAndRoundTripsProfile()
    {
        var saved = await _service.SaveAsync(new SessionProfile
        {
            Name = "Web + MCP",
            Model = "gpt-5.2-codex",
            IsAutopilot = false,
            IncludeWellKnownMcpConfigs = true,
            EnabledMcpServers = "context7",
        });

        var loaded = await _service.GetAsync(saved.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Web + MCP", loaded!.Name);
        Assert.Equal("context7", loaded.EnabledMcpServers);
    }

    [Fact]
    public async Task DeleteAsync_RemovesProfile()
    {
        var profile = await _service.SaveAsync(new SessionProfile { Name = "Temp" });
        await _service.DeleteAsync(profile.Id);

        var profiles = await _service.ListAsync();
        Assert.DoesNotContain(profiles, p => p.Id == profile.Id);
        Assert.Contains(profiles, p => p.Id == "default");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
