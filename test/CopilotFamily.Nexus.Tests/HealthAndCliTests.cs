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

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("status")]
    [InlineData("install")]
    [InlineData("update")]
    [InlineData("publish")]
    [InlineData("winapp")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void IsCliCommand_RecognizesValidCommands(string command)
    {
        Assert.True(Program.IsCliCommand([command]));
    }

    [Theory]
    [InlineData("START")]
    [InlineData("Status")]
    [InlineData("STOP")]
    [InlineData("Install")]
    [InlineData("UPDATE")]
    [InlineData("Publish")]
    public void IsCliCommand_IsCaseInsensitive(string command)
    {
        Assert.True(Program.IsCliCommand([command]));
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

    private record HealthPayload(string? Status, int Sessions, int Models, string? Uptime);
}
