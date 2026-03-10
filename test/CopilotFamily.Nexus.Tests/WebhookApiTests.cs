namespace CopilotFamily.Nexus.Tests;

using System.Net;
using System.Net.Http.Json;
using CopilotFamily.Core.Contracts;
using Xunit;

/// <summary>
/// Integration tests for the Webhook API endpoints.
/// </summary>
public class WebhookApiTests : IClassFixture<NexusTestFactory>
{
    private readonly HttpClient _client;

    public WebhookApiTests(NexusTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateSessionWithMessage_ReturnsCreated()
    {
        var request = new WebhookCreateSessionRequest
        {
            Message = "Hello from webhook",
            IsAutopilot = true,
        };
        var response = await _client.PostAsJsonAsync("/api/webhooks/sessions", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<SessionInfoDto>();
        Assert.NotNull(dto);
        Assert.NotEmpty(dto.Id);
    }

    [Fact]
    public async Task CreateSessionWithMessage_EmptyMessage_ReturnsBadRequest()
    {
        var request = new WebhookCreateSessionRequest { Message = "" };
        var response = await _client.PostAsJsonAsync("/api/webhooks/sessions", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendMessageToSession_ReturnsAccepted()
    {
        // First create a session
        var createRequest = new WebhookCreateSessionRequest { Message = "Initial" };
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks/sessions", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();

        // Then send another message
        var msgRequest = new WebhookMessageRequest { Message = "Follow up" };
        var response = await _client.PostAsJsonAsync($"/api/webhooks/sessions/{created!.Id}/message", msgRequest);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task SendMessageToSession_NotFound()
    {
        var request = new WebhookMessageRequest { Message = "Hello" };
        var response = await _client.PostAsJsonAsync("/api/webhooks/sessions/nonexistent/message", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SendMessageToSession_EmptyMessage_ReturnsBadRequest()
    {
        var createRequest = new WebhookCreateSessionRequest { Message = "Initial" };
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks/sessions", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();

        var request = new WebhookMessageRequest { Message = "" };
        var response = await _client.PostAsJsonAsync($"/api/webhooks/sessions/{created!.Id}/message", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AbortSession_ReturnsOk()
    {
        var createRequest = new WebhookCreateSessionRequest { Message = "Initial" };
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks/sessions", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();

        var response = await _client.PostAsync($"/api/webhooks/sessions/{created!.Id}/abort", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AbortSession_NotFound()
    {
        var response = await _client.PostAsync("/api/webhooks/sessions/nonexistent/abort", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
