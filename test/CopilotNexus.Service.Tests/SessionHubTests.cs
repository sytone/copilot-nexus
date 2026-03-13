namespace CopilotNexus.Service.Tests;

using System.Linq;
using System.Net.Http.Json;
using CopilotNexus.Core.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

public class SessionHubTests : IClassFixture<NexusTestFactory>, IAsyncLifetime
{
    private readonly NexusTestFactory _factory;
    private readonly HttpClient _client;
    private readonly HubConnection _hubConnection;

    public SessionHubTests(NexusTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(_client.BaseAddress!, "/hubs/session"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _hubConnection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
        _client.Dispose();
    }

    [Fact]
    public async Task SendInput_LongRunningPrompt_ReturnsBeforeClientCancellation()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest
        {
            Name = "Hub Timeout Session",
            IsAutopilot = true,
        });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>();
        Assert.NotNull(created);

        await _hubConnection.InvokeAsync("JoinSession", created!.Id);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await _hubConnection.InvokeAsync("SendInput", created.Id, BuildLongPrompt(), timeoutCts.Token);
    }

    private static string BuildLongPrompt(int words = 60)
    {
        return string.Join(" ", Enumerable.Repeat("timeout-regression", words));
    }
}
