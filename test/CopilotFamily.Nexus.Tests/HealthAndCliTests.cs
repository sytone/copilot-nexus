namespace CopilotFamily.Nexus.Tests;

using System.Net;
using System.Net.Http.Json;
using Xunit;

/// <summary>
/// Tests for the health endpoint and CLI command routing.
/// </summary>
public class HealthAndCliTests : IClassFixture<NexusTestFactory>
{
    private readonly HttpClient _client;

    public HealthAndCliTests(NexusTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsRunningStatus()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.NotNull(body);
        Assert.Equal("Running", body!.Status);
    }

    [Fact]
    public async Task HealthEndpoint_ReportsSessionCount()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthPayload>();

        Assert.NotNull(body);
        Assert.True(body!.Sessions >= 0);
    }

    [Fact]
    public async Task HealthEndpoint_ReportsModelCount()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthPayload>();

        Assert.NotNull(body);
        Assert.True(body!.Models >= 0);
    }

    [Fact]
    public async Task HealthEndpoint_ReportsUptime()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthPayload>();

        Assert.NotNull(body);
        Assert.NotNull(body!.Uptime);
        Assert.True(DateTime.TryParse(body.Uptime, out _));
    }

    [Fact]
    public void IsCliCommand_RecognizesStartCommand()
    {
        Assert.True(Program.IsCliCommand(["start"]));
    }

    [Fact]
    public void IsCliCommand_RecognizesStatusCommand()
    {
        Assert.True(Program.IsCliCommand(["status"]));
    }

    [Fact]
    public void IsCliCommand_RecognizesWinappCommand()
    {
        Assert.True(Program.IsCliCommand(["winapp"]));
    }

    [Fact]
    public void IsCliCommand_RecognizesHelpFlag()
    {
        Assert.True(Program.IsCliCommand(["--help"]));
        Assert.True(Program.IsCliCommand(["-h"]));
        Assert.True(Program.IsCliCommand(["-?"]));
    }

    [Fact]
    public void IsCliCommand_ReturnsFalse_ForEmptyArgs()
    {
        Assert.False(Program.IsCliCommand([]));
    }

    [Fact]
    public void IsCliCommand_ReturnsFalse_ForUnknownArgs()
    {
        Assert.False(Program.IsCliCommand(["--urls", "http://localhost:5280"]));
    }

    [Fact]
    public void IsCliCommand_IsCaseInsensitive()
    {
        Assert.True(Program.IsCliCommand(["START"]));
        Assert.True(Program.IsCliCommand(["Status"]));
    }

    private record HealthPayload(string? Status, int Sessions, int Models, string? Uptime);
}
