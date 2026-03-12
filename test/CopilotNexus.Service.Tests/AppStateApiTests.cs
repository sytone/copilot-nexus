namespace CopilotNexus.Service.Tests;

using System.Net;
using System.Net.Http.Json;
using CopilotNexus.Core.Models;
using Xunit;

public class AppStateApiTests : IClassFixture<NexusTestFactory>
{
    private readonly HttpClient _client;

    public AppStateApiTests(NexusTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetState_ReturnsNoContent_WhenNotSaved()
    {
        await _client.DeleteAsync("/api/app-state");

        var response = await _client.GetAsync("/api/app-state");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PutThenGetState_RoundTripsState()
    {
        var state = new AppState
        {
            SessionCounter = 3,
            SelectedTabIndex = 0,
            Tabs = new List<TabState>
            {
                new() { Name = "Session 1", SdkSessionId = "sdk-1", Model = "gpt-4.1", IsAutopilot = true },
            },
        };

        var putResponse = await _client.PutAsJsonAsync("/api/app-state", state);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var getResponse = await _client.GetAsync("/api/app-state");
        getResponse.EnsureSuccessStatusCode();
        var loaded = await getResponse.Content.ReadFromJsonAsync<AppState>();

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.SessionCounter);
        Assert.Single(loaded.Tabs);
        Assert.Equal("sdk-1", loaded.Tabs[0].SdkSessionId);
    }

    [Fact]
    public async Task DeleteState_ClearsPersistedState()
    {
        var state = new AppState
        {
            SessionCounter = 1,
            Tabs = new List<TabState> { new() { Name = "Session 1", SdkSessionId = "sdk-delete" } },
        };
        await _client.PutAsJsonAsync("/api/app-state", state);

        var deleteResponse = await _client.DeleteAsync("/api/app-state");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync("/api/app-state");
        Assert.Equal(HttpStatusCode.NoContent, getResponse.StatusCode);
    }
}
