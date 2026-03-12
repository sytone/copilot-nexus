namespace CopilotNexus.Service.Tests;

using System.Net;
using System.Net.Http.Json;
using CopilotNexus.Core.Models;
using Xunit;

public class SessionProfilesApiTests : IClassFixture<NexusTestFactory>
{
    private readonly HttpClient _client;

    public SessionProfilesApiTests(NexusTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListProfiles_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/session-profiles");
        response.EnsureSuccessStatusCode();

        var profiles = await response.Content.ReadFromJsonAsync<List<SessionProfile>>();
        Assert.NotNull(profiles);
        Assert.NotEmpty(profiles!);
    }

    [Fact]
    public async Task CreateUpdateDeleteProfile_Works()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/session-profiles", new SessionProfile
        {
            Name = "Integration Profile",
            Model = "gpt-4.1",
            IsAutopilot = false,
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<SessionProfile>();
        Assert.NotNull(created);

        var updateResponse = await _client.PutAsJsonAsync($"/api/session-profiles/{created!.Id}", new SessionProfile
        {
            Name = "Updated Profile",
            Model = "gpt-5.2-codex",
            IsAutopilot = true,
        });
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<SessionProfile>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Profile", updated!.Name);

        var deleteResponse = await _client.DeleteAsync($"/api/session-profiles/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }
}
