namespace CopilotNexus.App.Tests.Services;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CopilotNexus.App.Services;
using CopilotNexus.Core.Contracts;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class NexusSessionManagerTests : IAsyncDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly NexusSessionManager _manager;

    public NexusSessionManagerTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        var logger = NullLogger.Instance;
        _manager = new NexusSessionManager("http://localhost:5280", logger, _mockHandler);
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync();
    }

    [Fact]
    public void Sessions_InitiallyEmpty()
    {
        Assert.Empty(_manager.Sessions);
    }

    [Fact]
    public void AvailableModels_InitiallyEmpty()
    {
        Assert.Empty(_manager.AvailableModels);
    }

    [Fact]
    public async Task LoadModelsAsync_PopulatesAvailableModelsFromApi()
    {
        var models = new List<ModelInfoDto>
        {
            new() { ModelId = "gpt-4.1", Name = "GPT-4.1", Capabilities = new List<string> { "reasoning" } },
            new() { ModelId = "gpt-5.2-codex", Name = "GPT-5.2 Codex", Capabilities = new List<string> { "reasoning" } },
        };

        _mockHandler.SetResponse("/api/models", HttpMethod.Get, HttpStatusCode.OK, JsonSerializer.Serialize(models));

        await _manager.LoadModelsAsync();

        Assert.Equal(2, _manager.AvailableModels.Count);
        Assert.Equal("gpt-4.1", _manager.AvailableModels[0].ModelId);
        Assert.Equal("gpt-5.2-codex", _manager.AvailableModels[1].ModelId);
    }

    [Fact]
    public async Task CreateSessionAsync_SendsPostRequest_ReturnsSessionInfo()
    {
        var dto = new SessionInfoDto
        {
            Id = "abc123",
            Name = "Test Session",
            Model = "gpt-4",
            SdkSessionId = "sdk-abc",
            IsAutopilot = true,
            State = "Running",
        };

        _mockHandler.SetResponse("/api/sessions", HttpMethod.Post, HttpStatusCode.OK,
            JsonSerializer.Serialize(dto));

        var config = new SessionConfiguration { Model = "gpt-4", IsAutopilot = true };
        var result = await _manager.CreateSessionAsync("Test Session", config);

        Assert.Equal("abc123", result.Id);
        Assert.Equal("Test Session", result.Name);
        Assert.Equal("gpt-4", result.Model);
        Assert.True(result.IsAutopilot);
    }

    [Fact]
    public async Task CreateSessionAsync_AddsSessionToLocalCache()
    {
        var dto = new SessionInfoDto
        {
            Id = "s1",
            Name = "Session 1",
            SdkSessionId = "sdk-s1",
            State = "Running",
        };

        _mockHandler.SetResponse("/api/sessions", HttpMethod.Post, HttpStatusCode.OK,
            JsonSerializer.Serialize(dto));

        await _manager.CreateSessionAsync("Session 1");

        Assert.Single(_manager.Sessions);
        Assert.Equal("s1", _manager.Sessions[0].Id);
    }

    [Fact]
    public async Task CreateSessionAsync_CreatesProxy_GetSessionReturnsIt()
    {
        var dto = new SessionInfoDto
        {
            Id = "s1",
            Name = "Session 1",
            SdkSessionId = "sdk-s1",
            State = "Running",
        };

        _mockHandler.SetResponse("/api/sessions", HttpMethod.Post, HttpStatusCode.OK,
            JsonSerializer.Serialize(dto));

        await _manager.CreateSessionAsync("Session 1");

        var proxy = _manager.GetSession("s1");
        Assert.NotNull(proxy);
        Assert.IsType<NexusSessionProxy>(proxy);
    }

    [Fact]
    public void GetSession_UnknownId_ReturnsNull()
    {
        var result = _manager.GetSession("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveSessionAsync_SendsDeleteRequest_RemovesFromCache()
    {
        // First create a session
        var dto = new SessionInfoDto
        {
            Id = "s1",
            Name = "Session 1",
            SdkSessionId = "sdk-s1",
            State = "Running",
        };
        _mockHandler.SetResponse("/api/sessions", HttpMethod.Post, HttpStatusCode.OK,
            JsonSerializer.Serialize(dto));
        await _manager.CreateSessionAsync("Session 1");

        Assert.Single(_manager.Sessions);

        // Now delete it
        _mockHandler.SetResponse("/api/sessions/s1", HttpMethod.Delete, HttpStatusCode.OK, "{}");
        await _manager.RemoveSessionAsync("s1");

        Assert.Empty(_manager.Sessions);
        Assert.Null(_manager.GetSession("s1"));
    }

    [Fact]
    public async Task ReconfigureSessionAsync_SendsPutRequest_UpdatesCache()
    {
        // Create session first
        var createDto = new SessionInfoDto
        {
            Id = "s1", Name = "Session 1", Model = "gpt-4",
            SdkSessionId = "sdk-s1", State = "Running",
        };
        _mockHandler.SetResponse("/api/sessions", HttpMethod.Post, HttpStatusCode.OK,
            JsonSerializer.Serialize(createDto));
        await _manager.CreateSessionAsync("Session 1");

        // Reconfigure it
        var updatedDto = new SessionInfoDto
        {
            Id = "s1", Name = "Session 1", Model = "claude-sonnet",
            SdkSessionId = "sdk-s1", State = "Running",
        };
        _mockHandler.SetResponse("/api/sessions/s1/configure", HttpMethod.Put, HttpStatusCode.OK,
            JsonSerializer.Serialize(updatedDto));

        var config = new SessionConfiguration { Model = "claude-sonnet" };
        var result = await _manager.ReconfigureSessionAsync("s1", config);

        Assert.Equal("claude-sonnet", result.Model);
        Assert.Equal("claude-sonnet", _manager.Sessions[0].Model);
    }

    [Fact]
    public async Task SendInputAsync_FallsToRest_WhenNoHubConnection()
    {
        // Without calling InitializeAsync, there's no hub connection — should use REST fallback
        _mockHandler.SetResponse("/api/sessions/s1/input", HttpMethod.Post, HttpStatusCode.OK, "{}");

        await _manager.SendInputAsync("s1", "test input");

        var lastRequest = _mockHandler.LastRequest;
        Assert.NotNull(lastRequest);
        Assert.Equal("/api/sessions/s1/input", lastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ResumeSessionAsync_SendsSdkSessionIdInCreateRequest()
    {
        var dto = new SessionInfoDto
        {
            Id = "s1", Name = "Resumed Session",
            SdkSessionId = "sdk-old", State = "Running",
        };
        _mockHandler.SetResponse("/api/sessions", HttpMethod.Post, HttpStatusCode.OK,
            JsonSerializer.Serialize(dto));

        var result = await _manager.ResumeSessionAsync("Resumed Session", "sdk-old");

        Assert.Equal("s1", result.Id);
        Assert.Equal("Resumed Session", result.Name);
        Assert.NotNull(_mockHandler.LastRequest);
        var requestBody = await _mockHandler.LastRequest!.Content!.ReadAsStringAsync();
        using var bodyJson = JsonDocument.Parse(requestBody);
        Assert.Equal("sdk-old", bodyJson.RootElement.GetProperty("sdkSessionId").GetString());
    }

    [Fact]
    public async Task CreateMultipleSessions_AllTracked()
    {
        for (int i = 1; i <= 3; i++)
        {
            var dto = new SessionInfoDto
            {
                Id = $"s{i}", Name = $"Session {i}",
                SdkSessionId = $"sdk-{i}", State = "Running",
            };
            _mockHandler.SetResponse("/api/sessions", HttpMethod.Post, HttpStatusCode.OK,
                JsonSerializer.Serialize(dto));
            await _manager.CreateSessionAsync($"Session {i}");
        }

        Assert.Equal(3, _manager.Sessions.Count);
        Assert.NotNull(_manager.GetSession("s1"));
        Assert.NotNull(_manager.GetSession("s2"));
        Assert.NotNull(_manager.GetSession("s3"));
    }

    [Fact]
    public async Task DisposeAsync_DisposesHttpClient()
    {
        await _manager.DisposeAsync();

        // After disposal, calls should throw
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _manager.CreateSessionAsync("test"));
    }
}

/// <summary>
/// Mock HTTP handler for testing NexusSessionManager without real HTTP calls.
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode status, string content)> _responses = new();
    public HttpRequestMessage? LastRequest { get; private set; }

    public void SetResponse(string path, HttpMethod method, HttpStatusCode status, string content)
    {
        _responses[$"{method}:{path}"] = (status, content);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var key = $"{request.Method}:{request.RequestUri!.AbsolutePath}";

        if (_responses.TryGetValue(key, out var response))
        {
            return Task.FromResult(new HttpResponseMessage(response.status)
            {
                Content = new StringContent(response.content, System.Text.Encoding.UTF8, "application/json"),
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
