namespace CopilotNexus.E2E.Tests;

using System.Net;
using System.Net.Http.Json;
using CopilotNexus.Core.Contracts;
using Xunit;

[Collection("E2E")]
public class RunningServerApiE2ETests
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri(E2ETestSettings.BaseUrl),
        Timeout = TimeSpan.FromSeconds(30),
    };

    [E2EFact]
    public async Task HealthEndpoint_IsReachable()
    {
        var response = await Client.GetAsync("/health");
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected /health success from {E2ETestSettings.BaseUrl}, got {(int)response.StatusCode} {response.StatusCode}. Body: {await ReadBodyAsync(response)}");
    }

    [E2EFact]
    public async Task ModelsEndpoint_ReturnsCatalog()
    {
        var response = await Client.GetAsync("/api/models");
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected /api/models success, got {(int)response.StatusCode} {response.StatusCode}. Body: {await ReadBodyAsync(response)}");

        var models = await response.Content.ReadFromJsonAsync<List<ModelInfoDto>>();
        Assert.NotNull(models);
        Assert.NotEmpty(models);
    }

    [E2EFact]
    public async Task SessionsApi_SmokeRoundTrip_DoesNotReturn500()
    {
        var name = $"E2E-{Guid.NewGuid():N}"[..12];
        var createRequest = new CreateSessionRequest
        {
            Name = name,
            IsAutopilot = true,
        };

        var createResponse = await Client.PostAsJsonAsync("/api/sessions", createRequest);
        Assert.NotEqual(HttpStatusCode.InternalServerError, createResponse.StatusCode);
        Assert.True(
            createResponse.IsSuccessStatusCode,
            $"Create session failed: {(int)createResponse.StatusCode} {createResponse.StatusCode}. Body: {await ReadBodyAsync(createResponse)}");

        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.Id));

        try
        {
            var getResponse = await Client.GetAsync($"/api/sessions/{created.Id}");
            Assert.True(
                getResponse.IsSuccessStatusCode,
                $"Get session failed: {(int)getResponse.StatusCode} {getResponse.StatusCode}. Body: {await ReadBodyAsync(getResponse)}");

            var sendResponse = await Client.PostAsJsonAsync(
                $"/api/sessions/{created.Id}/input",
                new SendInputRequest { Input = "E2E ping" });
            Assert.NotEqual(HttpStatusCode.InternalServerError, sendResponse.StatusCode);
            Assert.True(
                sendResponse.IsSuccessStatusCode,
                $"Send input failed: {(int)sendResponse.StatusCode} {sendResponse.StatusCode}. Body: {await ReadBodyAsync(sendResponse)}");

            var historyResponse = await Client.GetAsync($"/api/sessions/{created.Id}/history");
            Assert.True(
                historyResponse.IsSuccessStatusCode,
                $"History fetch failed: {(int)historyResponse.StatusCode} {historyResponse.StatusCode}. Body: {await ReadBodyAsync(historyResponse)}");

            var abortResponse = await Client.PostAsync($"/api/sessions/{created.Id}/abort", content: null);
            Assert.NotEqual(HttpStatusCode.InternalServerError, abortResponse.StatusCode);
            Assert.True(
                abortResponse.IsSuccessStatusCode,
                $"Abort failed: {(int)abortResponse.StatusCode} {abortResponse.StatusCode}. Body: {await ReadBodyAsync(abortResponse)}");

            var renameResponse = await Client.PutAsJsonAsync(
                $"/api/sessions/{created.Id}/name",
                new RenameSessionRequest { Name = $"{name}-renamed" });
            Assert.True(
                renameResponse.IsSuccessStatusCode,
                $"Rename failed: {(int)renameResponse.StatusCode} {renameResponse.StatusCode}. Body: {await ReadBodyAsync(renameResponse)}");
        }
        finally
        {
            var deleteResponse = await Client.DeleteAsync($"/api/sessions/{created.Id}");
            Assert.True(
                deleteResponse.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound,
                $"Delete failed: {(int)deleteResponse.StatusCode} {deleteResponse.StatusCode}. Body: {await ReadBodyAsync(deleteResponse)}");
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response)
        => await response.Content.ReadAsStringAsync();
}
