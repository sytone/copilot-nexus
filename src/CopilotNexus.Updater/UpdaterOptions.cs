namespace CopilotFamily.Updater;

/// <summary>
/// Options for the updater process.
/// </summary>
public class UpdaterOptions
{
    /// <summary>PID of the application process to wait for before updating.</summary>
    public int AppPid { get; set; }

    /// <summary>Directory where the application is installed.</summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>Directory where staged update files are located.</summary>
    public string StagingPath { get; set; } = string.Empty;

    /// <summary>Full path to the application executable to relaunch after update.</summary>
    public string AppExe { get; set; } = string.Empty;

    /// <summary>Seconds to wait for the app process to exit before aborting. Default: 30.</summary>
    public int WaitTimeoutSeconds { get; set; } = 30;
}
