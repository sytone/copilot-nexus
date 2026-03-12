namespace CopilotNexus.App.Services;

using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;

/// <summary>
/// Lightweight in-memory profile service for app test mode.
/// </summary>
public class InMemorySessionProfileService : ISessionProfileService
{
    private readonly List<SessionProfile> _profiles =
    [
        new SessionProfile
        {
            Id = "default",
            Name = "Default",
            Description = "Default test profile",
            IsAutopilot = true,
            IncludeWellKnownMcpConfigs = true,
        },
    ];

    public Task<IReadOnlyList<SessionProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SessionProfile> profiles = _profiles
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(profiles);
    }

    public Task<SessionProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var profile = _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(profile);
    }

    public Task<SessionProfile> SaveAsync(SessionProfile profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
            profile.Id = Guid.NewGuid().ToString("N")[..8];
        profile.UpdatedAtUtc = DateTime.UtcNow;

        var index = _profiles.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _profiles[index] = profile;
        }
        else
        {
            _profiles.Add(profile);
        }
        return Task.FromResult(profile);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _profiles.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (_profiles.Count == 0)
        {
            _profiles.Add(new SessionProfile
            {
                Id = "default",
                Name = "Default",
                Description = "Default test profile",
                IsAutopilot = true,
                IncludeWellKnownMcpConfigs = true,
            });
        }
        return Task.CompletedTask;
    }
}
