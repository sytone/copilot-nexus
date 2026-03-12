namespace CopilotNexus.Core.Services;

using System.Text.Json;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// JSON-backed profile service for Nexus-owned session profiles.
/// </summary>
public class JsonSessionProfileService : ISessionProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly SessionProfile DefaultProfile = new()
    {
        Id = "default",
        Name = "Default",
        Description = "Default Nexus profile",
        IsAutopilot = true,
        IncludeWellKnownMcpConfigs = true,
    };

    private readonly string _filePath;
    private readonly ILogger<JsonSessionProfileService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSessionProfileService(ILogger<JsonSessionProfileService> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? CopilotNexusPaths.NexusSessionProfilesFile;
    }

    public async Task<IReadOnlyList<SessionProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.Profiles
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SessionProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SessionProfile> SaveAsync(SessionProfile profile, CancellationToken cancellationToken = default)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("Profile name is required.", nameof(profile));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);

            profile.Id = string.IsNullOrWhiteSpace(profile.Id)
                ? Guid.NewGuid().ToString("N")[..8]
                : profile.Id.Trim();
            profile.Name = profile.Name.Trim();
            profile.UpdatedAtUtc = DateTime.UtcNow;

            var existingIndex = state.Profiles.FindIndex(p =>
                string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                state.Profiles[existingIndex] = profile;
            }
            else
            {
                state.Profiles.Add(profile);
            }

            await SaveStateAsync(state, cancellationToken);
            return profile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            state.Profiles.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            EnsureDefaultProfile(state);
            await SaveStateAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SessionProfilesState> LoadStateAsync(CancellationToken cancellationToken)
    {
        var tmpPath = _filePath + ".tmp";
        if (!File.Exists(_filePath) && File.Exists(tmpPath))
        {
            try
            {
                File.Move(tmpPath, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recover profile state from temp file: {Path}", tmpPath);
            }
        }

        if (!File.Exists(_filePath))
        {
            var initial = new SessionProfilesState();
            EnsureDefaultProfile(initial);
            return initial;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var state = JsonSerializer.Deserialize<SessionProfilesState>(json, JsonOptions)
                ?? new SessionProfilesState();
            EnsureDefaultProfile(state);
            return state;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid profiles JSON at {Path}; resetting to defaults", _filePath);
            var reset = new SessionProfilesState();
            EnsureDefaultProfile(reset);
            return reset;
        }
    }

    private async Task SaveStateAsync(SessionProfilesState state, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        var tmpPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(tmpPath, json, cancellationToken);
        File.Move(tmpPath, _filePath, overwrite: true);
    }

    private static void EnsureDefaultProfile(SessionProfilesState state)
    {
        if (state.Profiles.Count == 0)
        {
            state.Profiles.Add(Clone(DefaultProfile));
            return;
        }

        var hasDefault = state.Profiles.Any(p =>
            string.Equals(p.Id, DefaultProfile.Id, StringComparison.OrdinalIgnoreCase));
        if (!hasDefault)
        {
            state.Profiles.Insert(0, Clone(DefaultProfile));
        }
    }

    private static SessionProfile Clone(SessionProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Description = profile.Description,
        Model = profile.Model,
        IsAutopilot = profile.IsAutopilot,
        WorkingDirectory = profile.WorkingDirectory,
        AgentFilePath = profile.AgentFilePath,
        IncludeWellKnownMcpConfigs = profile.IncludeWellKnownMcpConfigs,
        AdditionalMcpConfigPaths = profile.AdditionalMcpConfigPaths,
        EnabledMcpServers = profile.EnabledMcpServers,
        AdditionalSkillDirectories = profile.AdditionalSkillDirectories,
        UpdatedAtUtc = profile.UpdatedAtUtc,
    };
}
