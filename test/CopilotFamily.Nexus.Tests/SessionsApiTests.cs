namespace CopilotFamily.Nexus.Tests;

using System.Net;
using System.Net.Http.Json;
using CopilotFamily.Core.Contracts;
using Xunit;

/// <summary>
/// Integration tests for the REST Sessions API.
/// Uses an in-memory TestServer with mock SDK services.
/// </summary>
public class SessionsApiTests : IClassFixture<NexusTestFactory>
{
    private readonly HttpClient _client;

    public SessionsApiTests(NexusTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListSessions_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/sessions");
        response.EnsureSuccessStatusCode();

        var sessions = await response.Content.ReadFromJsonAsync<List<SessionInfoDto>>();
        Assert.NotNull(sessions);
    }

    [Fact]
    public async Task CreateSession_ReturnsCreated()
    {
        var request = new CreateSessionRequest { Name = "Test Session", IsAutopilot = true };
        var response = await _client.PostAsJsonAsync("/api/sessions", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<SessionInfoDto>();
        Assert.NotNull(dto);
        Assert.Equal("Test Session", dto.Name);
        Assert.NotEmpty(dto.Id);
    }

    [Fact]
    public async Task CreateSession_ThenGetById()
    {
        var request = new CreateSessionRequest { Name = "Get By Id Test" };
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", request);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();

        var getResponse = await _client.GetAsync($"/api/sessions/{created!.Id}");
        getResponse.EnsureSuccessStatusCode();

        var dto = await getResponse.Content.ReadFromJsonAsync<SessionInfoDto>();
        Assert.Equal(created.Id, dto!.Id);
        Assert.Equal("Get By Id Test", dto.Name);
    }

    [Fact]
    public async Task CreateSession_ThenListIncludes()
    {
        var request = new CreateSessionRequest { Name = "Listed Session" };
        await _client.PostAsJsonAsync("/api/sessions", request);

        var response = await _client.GetAsync("/api/sessions");
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionInfoDto>>();

        Assert.Contains(sessions!, s => s.Name == "Listed Session");
    }

    [Fact]
    public async Task DeleteSession_ReturnsNoContent()
    {
        var request = new CreateSessionRequest { Name = "To Delete" };
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", request);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();

        var deleteResponse = await _client.DeleteAsync($"/api/sessions/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteSession_NotFound()
    {
        var response = await _client.DeleteAsync("/api/sessions/nonexistent-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSession_NotFound()
    {
        var response = await _client.GetAsync("/api/sessions/nonexistent-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SendInput_ReturnsAccepted()
    {
        var createRequest = new CreateSessionRequest { Name = "Input Session" };
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();

        var inputRequest = new SendInputRequest { Input = "Hello" };
        var response = await _client.PostAsJsonAsync($"/api/sessions/{created!.Id}/input", inputRequest);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task SendInput_EmptyInput_ReturnsBadRequest()
    {
        var createRequest = new CreateSessionRequest { Name = "Empty Input Session" };
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();

        var inputRequest = new SendInputRequest { Input = "" };
        var response = await _client.PostAsJsonAsync($"/api/sessions/{created!.Id}/input", inputRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendInput_NotFound()
    {
        var inputRequest = new SendInputRequest { Input = "Hello" };
        var response = await _client.PostAsJsonAsync("/api/sessions/nonexistent/input", inputRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfigureSession_ChangesModel()
    {
        var createRequest = new CreateSessionRequest { Name = "Config Session" };
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();

        var configRequest = new ConfigureSessionRequest { Model = "gpt-5" };
        var response = await _client.PutAsJsonAsync($"/api/sessions/{created!.Id}/configure", configRequest);

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<SessionInfoDto>();
        Assert.Equal("gpt-5", dto!.Model);
    }
}
