namespace CopilotNexus.App.Tests.Services;

using System.Net;
using System.Text.Json;
using CopilotNexus.App.Services;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class NexusStatePersistenceServiceTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly NexusStatePersistenceService _service;

    public NexusStatePersistenceServiceTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _service = new NexusStatePersistenceService(
            "http://localhost:5280",
            NullLogger<NexusStatePersistenceService>.Instance,
            _mockHandler);
    }

    [Fact]
    public async Task SaveAsync_SendsPutRequestToApi()
    {
        _mockHandler.SetResponse("/api/app-state", HttpMethod.Put, HttpStatusCode.NoContent, string.Empty);

        var state = new AppState
        {
            SessionCounter = 1,
            Tabs = new List<TabState> { new() { Name = "Session 1", SdkSessionId = "sdk-1" } },
        };

        await _service.SaveAsync(state);

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Equal(HttpMethod.Put, _mockHandler.LastRequest!.Method);
        Assert.Equal("/api/app-state", _mockHandler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNullWhenNoContent()
    {
        _mockHandler.SetResponse("/api/app-state", HttpMethod.Get, HttpStatusCode.NoContent, string.Empty);

        var state = await _service.LoadAsync();

        Assert.Null(state);
    }

    [Fact]
    public async Task LoadAsync_ReturnsStateFromApi()
    {
        var payload = new AppState
        {
            SessionCounter = 2,
            SelectedTabIndex = 0,
            Tabs = new List<TabState> { new() { Name = "Session 2", SdkSessionId = "sdk-2" } },
        };
        _mockHandler.SetResponse("/api/app-state", HttpMethod.Get, HttpStatusCode.OK, JsonSerializer.Serialize(payload));

        var state = await _service.LoadAsync();

        Assert.NotNull(state);
        Assert.Equal(2, state!.SessionCounter);
        Assert.Single(state.Tabs);
        Assert.Equal("sdk-2", state.Tabs[0].SdkSessionId);
    }

    [Fact]
    public async Task ClearAsync_SendsDeleteRequest()
    {
        _mockHandler.SetResponse("/api/app-state", HttpMethod.Delete, HttpStatusCode.NoContent, string.Empty);

        await _service.ClearAsync();

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Equal(HttpMethod.Delete, _mockHandler.LastRequest!.Method);
        Assert.Equal("/api/app-state", _mockHandler.LastRequest.RequestUri!.AbsolutePath);
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
