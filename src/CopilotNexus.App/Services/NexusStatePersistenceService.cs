namespace CopilotNexus.App.Services;

using System.Net;
using System.Net.Http.Json;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Persists application state through Nexus APIs.
/// Production app mode uses this so state ownership lives in Nexus.
/// </summary>
public sealed class NexusStatePersistenceService : IStatePersistenceService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NexusStatePersistenceService> _logger;

    public NexusStatePersistenceService(string nexusBaseUrl, ILogger<NexusStatePersistenceService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { BaseAddress = new Uri(nexusBaseUrl.TrimEnd('/')) };
    }

    internal NexusStatePersistenceService(string nexusBaseUrl, ILogger<NexusStatePersistenceService> logger, HttpMessageHandler handler)
    {
        _logger = logger;
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(nexusBaseUrl.TrimEnd('/')) };
    }

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/app-state", state, cancellationToken);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Saved app state to Nexus ({TabCount} tabs)", state.Tabs.Count);
    }

    public async Task<AppState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/app-state", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            _logger.LogDebug("No Nexus app state available.");
            return null;
        }

        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<AppState>(cancellationToken: cancellationToken);
        _logger.LogInformation("Loaded app state from Nexus ({TabCount} tabs)", state?.Tabs.Count ?? 0);
        return state;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync("/api/app-state", cancellationToken);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Cleared app state in Nexus");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
