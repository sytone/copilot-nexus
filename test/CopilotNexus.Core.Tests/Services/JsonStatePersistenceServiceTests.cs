namespace CopilotNexus.Core.Tests.Services;

using System.IO;
using System.Text.Json;
using CopilotNexus.Core.Models;
using CopilotNexus.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class JsonStatePersistenceServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFile;
    private readonly JsonStatePersistenceService _service;

    public JsonStatePersistenceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CopilotNexusTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _stateFile = Path.Combine(_tempDir, "session-state.json");
        _service = new JsonStatePersistenceService(
            NullLogger<JsonStatePersistenceService>.Instance, _stateFile);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task SaveAsync_CreatesFile()
    {
        var state = CreateSampleState();
        await _service.SaveAsync(state);
        Assert.True(File.Exists(_stateFile));
    }

    [Fact]
    public async Task RoundTrip_PreservesState()
    {
        var state = CreateSampleState();
        await _service.SaveAsync(state);
        var loaded = await _service.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(state.Version, loaded!.Version);
        Assert.Equal(state.SelectedTabIndex, loaded.SelectedTabIndex);
        Assert.Equal(state.SessionCounter, loaded.SessionCounter);
        Assert.Equal(state.Tabs.Count, loaded.Tabs.Count);
        Assert.Equal(state.Tabs[0].Name, loaded.Tabs[0].Name);
    }

    [Fact]
    public async Task RoundTrip_PreservesSdkSessionId()
    {
        var state = CreateSampleState();
        await _service.SaveAsync(state);
        var loaded = await _service.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("copilot-nexus-1-1706932800", loaded!.Tabs[0].SdkSessionId);
        Assert.Equal("copilot-nexus-2-1706932801", loaded.Tabs[1].SdkSessionId);
    }

    [Fact]
    public async Task RoundTrip_PreservesModel()
    {
        var state = CreateSampleState();
        await _service.SaveAsync(state);
        var loaded = await _service.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("gpt-4.1", loaded!.Tabs[0].Model);
        Assert.Null(loaded.Tabs[1].Model);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        var result = await _service.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadAsync_RecoversFromTempFile_WhenPrimaryMissing()
    {
        var state = CreateSampleState();
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        await File.WriteAllTextAsync(_stateFile + ".tmp", json);

        var result = await _service.LoadAsync();

        Assert.NotNull(result);
        Assert.True(File.Exists(_stateFile));
        Assert.False(File.Exists(_stateFile + ".tmp"));
    }

    [Fact]
    public async Task LoadAsync_BackupsCorruptFile()
    {
        await File.WriteAllTextAsync(_stateFile, "NOT VALID JSON {{{{");
        var result = await _service.LoadAsync();

        Assert.Null(result);
        Assert.True(File.Exists(_stateFile + ".bak"));
    }

    [Fact]
    public async Task SaveAsync_UsesAtomicWrite()
    {
        await _service.SaveAsync(CreateSampleState());
        await _service.SaveAsync(CreateSampleState());

        Assert.True(File.Exists(_stateFile));
        Assert.False(File.Exists(_stateFile + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectory_WhenNotExists()
    {
        var deepDir = Path.Combine(_tempDir, "sub", "dir");
        var deepFile = Path.Combine(deepDir, "state.json");
        var svc = new JsonStatePersistenceService(
            NullLogger<JsonStatePersistenceService>.Instance, deepFile);

        await svc.SaveAsync(CreateSampleState());
        Assert.True(File.Exists(deepFile));
    }

    [Fact]
    public async Task SessionCounter_IsPreserved()
    {
        var state = CreateSampleState();
        state.SessionCounter = 42;
        await _service.SaveAsync(state);
        var loaded = await _service.LoadAsync();
        Assert.Equal(42, loaded!.SessionCounter);
    }

    [Fact]
    public async Task ClearAsync_DeletesStateFile()
    {
        await _service.SaveAsync(CreateSampleState());
        Assert.True(File.Exists(_stateFile));

        await _service.ClearAsync();

        Assert.False(File.Exists(_stateFile));
    }

    [Fact]
    public async Task ClearAsync_NoOp_WhenFileDoesNotExist()
    {
        Assert.False(File.Exists(_stateFile));
        await _service.ClearAsync(); // should not throw
        Assert.False(File.Exists(_stateFile));
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_AfterClear()
    {
        await _service.SaveAsync(CreateSampleState());
        await _service.ClearAsync();

        var result = await _service.LoadAsync();
        Assert.Null(result);
    }

    private static AppState CreateSampleState()
    {
        return new AppState
        {
            Version = 1,
            SelectedTabIndex = 0,
            SessionCounter = 3,
            Tabs = new List<TabState>
            {
                new()
                {
                    Name = "Session 1",
                    Model = "gpt-4.1",
                    SdkSessionId = "copilot-nexus-1-1706932800",
                },
                new()
                {
                    Name = "Session 2",
                    SdkSessionId = "copilot-nexus-2-1706932801",
                }
            }
        };
    }
}
