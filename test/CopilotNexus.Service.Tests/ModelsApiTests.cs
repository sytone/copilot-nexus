namespace CopilotNexus.Service.Tests;

using System.Net.Http.Json;
using CopilotNexus.Core.Contracts;
using Xunit;

/// <summary>
/// Integration tests for the Models API endpoint.
/// </summary>
public class ModelsApiTests : IClassFixture<NexusTestFactory>
{
    private readonly HttpClient _client;

    public ModelsApiTests(NexusTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListModels_ReturnsModels()
    {
        var response = await _client.GetAsync("/api/models");
        response.EnsureSuccessStatusCode();

        var models = await response.Content.ReadFromJsonAsync<List<ModelInfoDto>>();
        Assert.NotNull(models);
        Assert.NotEmpty(models);
    }

    [Fact]
    public async Task ListModels_IncludesExpectedFields()
    {
        var response = await _client.GetAsync("/api/models");
        var models = await response.Content.ReadFromJsonAsync<List<ModelInfoDto>>();

        var first = models!.First();
        Assert.NotEmpty(first.ModelId);
        Assert.NotEmpty(first.Name);
    }

}
