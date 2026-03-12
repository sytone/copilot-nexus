namespace CopilotNexus.App.Tests.Services;

using System.Net;
using System.Text.Json;
using CopilotNexus.App.Services;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class NexusSessionProfileServiceTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly NexusSessionProfileService _service;

    public NexusSessionProfileServiceTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _service = new NexusSessionProfileService(
            "http://localhost:5280",
            NullLogger<NexusSessionProfileService>.Instance,
            _mockHandler);
    }

    [Fact]
    public async Task ListAsync_ReturnsProfiles()
    {
        var payload =
            JsonSerializer.Serialize(new List<SessionProfile> { new() { Id = "default", Name = "Default" } });
        _mockHandler.SetResponse("/api/session-profiles", HttpMethod.Get, HttpStatusCode.OK, payload);

        var profiles = await _service.ListAsync();

        Assert.Single(profiles);
        Assert.Equal("Default", profiles[0].Name);
    }

    [Fact]
    public async Task SaveAsync_UsesPost_WhenIdIsEmpty()
    {
        var profile = new SessionProfile { Id = string.Empty, Name = "New Profile" };
        _mockHandler.SetResponse(
            "/api/session-profiles",
            HttpMethod.Post,
            HttpStatusCode.Created,
            JsonSerializer.Serialize(new SessionProfile { Id = "p1", Name = "New Profile" }));

        var saved = await _service.SaveAsync(profile);

        Assert.Equal("p1", saved.Id);
        Assert.Equal(HttpMethod.Post, _mockHandler.LastRequest!.Method);
    }

    [Fact]
    public async Task SaveAsync_UsesPut_WhenIdExists()
    {
        var profile = new SessionProfile { Id = "p1", Name = "Updated" };
        _mockHandler.SetResponse(
            "/api/session-profiles/p1",
            HttpMethod.Put,
            HttpStatusCode.OK,
            JsonSerializer.Serialize(profile));

        var saved = await _service.SaveAsync(profile);

        Assert.Equal("p1", saved.Id);
        Assert.Equal(HttpMethod.Put, _mockHandler.LastRequest!.Method);
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
