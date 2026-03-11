namespace CopilotFamily.Core.Services;

using System.Text.Json;
using CopilotFamily.Core.Interfaces;
using CopilotFamily.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Persists application state as JSON to the local app data folder.
/// Uses atomic writes (write to .tmp, then rename) to prevent corruption.
/// Only stores lightweight tab metadata — the SDK handles conversation history.
/// </summary>
public class JsonStatePersistenceService : IStatePersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _stateFilePath;
    private readonly ILogger<JsonStatePersistenceService> _logger;

    public JsonStatePersistenceService(ILogger<JsonStatePersistenceService> logger, string? stateFilePath = null)
    {
        _logger = logger;
        _stateFilePath = stateFilePath ?? GetDefaultStatePath();
    }

    public string StateFilePath => _stateFilePath;

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_stateFilePath)!;
        Directory.CreateDirectory(dir);

        var tmpPath = _stateFilePath + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(tmpPath, json, cancellationToken);

            // Atomic rename — overwrites existing file
            File.Move(tmpPath, _stateFilePath, overwrite: true);

            _logger.LogInformation("State saved ({TabCount} tabs, {Path})", state.Tabs.Count, _stateFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state to {Path}", _stateFilePath);

            // Clean up temp file on failure
            try { File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    public async Task<AppState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            _logger.LogDebug("No state file found at {Path}", _stateFilePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath, cancellationToken);
            var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions);

            if (state == null)
            {
                _logger.LogWarning("State file deserialized to null at {Path}", _stateFilePath);
                BackupCorruptFile();
                return null;
            }

            _logger.LogInformation("State loaded ({TabCount} tabs, {Path})", state.Tabs.Count, _stateFilePath);
            return state;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to restore session state from {Path}", _stateFilePath);
            BackupCorruptFile();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading state from {Path}", _stateFilePath);
            return null;
        }
    }

    private void BackupCorruptFile()
    {
        try
        {
            var bakPath = _stateFilePath + ".bak";
            File.Copy(_stateFilePath, bakPath, overwrite: true);
            File.Delete(_stateFilePath);
            _logger.LogWarning("Corrupt state file backed up to {BakPath}", bakPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup corrupt state file");
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                File.Delete(_stateFilePath);
                _logger.LogInformation("State file cleared: {Path}", _stateFilePath);
            }
            else
            {
                _logger.LogDebug("No state file to clear at {Path}", _stateFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear state file at {Path}", _stateFilePath);
        }

        return Task.CompletedTask;
    }

    private static string GetDefaultStatePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotFamily", "state", "session-state.json");
    }
}
