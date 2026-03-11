namespace CopilotNexus.Core.Interfaces;

/// <summary>
/// Watches for staged updates and notifies when a new version is available.
/// </summary>
public interface IUpdateDetectionService : IDisposable
{
    /// <summary>Raised when new files are detected in the staging folder.</summary>
    event EventHandler? UpdateAvailable;

    /// <summary>Whether an update is currently staged.</summary>
    bool IsUpdateStaged { get; }

    /// <summary>Starts watching the staging folder for changes.</summary>
    void StartWatching();

    /// <summary>Stops watching.</summary>
    void StopWatching();
}
