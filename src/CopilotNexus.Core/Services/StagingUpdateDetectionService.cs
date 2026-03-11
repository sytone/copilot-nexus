namespace CopilotNexus.Core.Services;

using System.IO;
using CopilotNexus.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// Watches dist/staging/ for new files using FileSystemWatcher + periodic timer fallback.
/// </summary>
public class StagingUpdateDetectionService : IUpdateDetectionService
{
    private readonly string _stagingPath;
    private readonly ILogger _logger;
    private FileSystemWatcher? _watcher;
    private Timer? _fallbackTimer;
    private bool _disposed;
    private bool _notified;

    public event EventHandler? UpdateAvailable;
    public bool IsUpdateStaged { get; private set; }

    public StagingUpdateDetectionService(string stagingPath, ILogger logger)
    {
        _stagingPath = stagingPath;
        _logger = logger;
    }

    public void StartWatching()
    {
        if (_disposed) return;

        Directory.CreateDirectory(_stagingPath);

        // FileSystemWatcher for immediate detection
        _watcher = new FileSystemWatcher(_stagingPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
        };
        _watcher.Created += OnStagingChanged;
        _watcher.Changed += OnStagingChanged;

        // Timer fallback every 30 seconds (FSW can miss events)
        _fallbackTimer = new Timer(_ => CheckStaging(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

        _logger.LogInformation("Watching for staged updates at {Path}", _stagingPath);
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
    }

    /// <summary>
    /// Resets the notification flag so the event can fire again (after user clicks "Later").
    /// </summary>
    public void ResetNotification()
    {
        _notified = false;
    }

    private void OnStagingChanged(object sender, FileSystemEventArgs e)
    {
        CheckStaging();
    }

    private void CheckStaging()
    {
        try
        {
            var hasFiles = Directory.Exists(_stagingPath)
                && Directory.EnumerateFiles(_stagingPath, "*", SearchOption.AllDirectories).Any();

            IsUpdateStaged = hasFiles;

            if (hasFiles && !_notified)
            {
                _notified = true;
                _logger.LogInformation("Staged update detected at {Path}", _stagingPath);
                UpdateAvailable?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking staging folder");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
        GC.SuppressFinalize(this);
    }
}
