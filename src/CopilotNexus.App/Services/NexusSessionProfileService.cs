namespace CopilotNexus.App.Services;

using System.Net;
using System.Net.Http.Json;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// ISessionProfileService implementation backed by Nexus REST APIs.
/// </summary>
public sealed class NexusSessionProfileService : ISessionProfileService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NexusSessionProfileService> _logger;

    public NexusSessionProfileService(string nexusBaseUrl, ILogger<NexusSessionProfileService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { BaseAddress = new Uri(nexusBaseUrl.TrimEnd('/')) };
    }

    internal NexusSessionProfileService(string nexusBaseUrl, ILogger<NexusSessionProfileService> logger, HttpMessageHandler handler)
    {
        _logger = logger;
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(nexusBaseUrl.TrimEnd('/')) };
    }

    public async Task<IReadOnlyList<SessionProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _httpClient.GetFromJsonAsync<List<SessionProfile>>(
                           "/api/session-profiles",
                           cancellationToken)
                       ?? new List<SessionProfile>();
        return profiles;
    }

    public async Task<SessionProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/session-profiles/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionProfile>(cancellationToken: cancellationToken);
    }

    public async Task<SessionProfile> SaveAsync(SessionProfile profile, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            response = await _httpClient.PostAsJsonAsync("/api/session-profiles", profile, cancellationToken);
        }
        else
        {
            response = await _httpClient.PutAsJsonAsync($"/api/session-profiles/{profile.Id}", profile, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<SessionProfile>(cancellationToken: cancellationToken);
        if (saved == null)
            throw new InvalidOperationException("Nexus returned an empty profile response.");
        return saved;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/session-profiles/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
