namespace CopilotNexus.Core.Interfaces;

using CopilotNexus.Core.Models;

/// <summary>
/// Saves and loads application state to/from persistent storage.
/// </summary>
public interface IStatePersistenceService
{
    /// <summary>Saves application state. Uses atomic write to prevent corruption.</summary>
    Task SaveAsync(AppState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads application state. Returns null if no state file exists.
    /// Backs up corrupt files with .bak extension and returns null.
    /// </summary>
    Task<AppState?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes persisted state, giving the app a clean slate on next load.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
