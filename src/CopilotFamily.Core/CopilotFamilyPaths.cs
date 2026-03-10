namespace CopilotFamily.Core;

/// <summary>
/// Centralized path definitions for the CopilotFamily installation layout.
/// All paths are rooted at %LOCALAPPDATA%\CopilotFamily\.
/// </summary>
public static class CopilotFamilyPaths
{
    /// <summary>Root directory: %LOCALAPPDATA%\CopilotFamily\</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotFamily");

    /// <summary>Nexus service install directory.</summary>
    public static string NexusInstall { get; } = Path.Combine(Root, "nexus");

    /// <summary>Desktop app install directory.</summary>
    public static string AppInstall { get; } = Path.Combine(Root, "app");

    /// <summary>Shared staging root — updates are staged here before being applied.</summary>
    public static string StagingRoot { get; } = Path.Combine(Root, "staging");

    /// <summary>Staging directory for Nexus updates.</summary>
    public static string NexusStaging { get; } = Path.Combine(StagingRoot, "nexus");

    /// <summary>Staging directory for App updates.</summary>
    public static string AppStaging { get; } = Path.Combine(StagingRoot, "app");

    /// <summary>Shared log directory.</summary>
    public static string Logs { get; } = Path.Combine(Root, "logs");

    /// <summary>PID lock file for the Nexus process.</summary>
    public static string NexusLockFile { get; } = Path.Combine(Root, "nexus.lock");

    /// <summary>Application state file (tab layout, session IDs).</summary>
    public static string AppStateFile { get; } = Path.Combine(Root, "app-state.json");

    /// <summary>Nexus executable path in the install directory.</summary>
    public static string NexusExe { get; } = Path.Combine(NexusInstall, "CopilotFamily.Nexus.exe");

    /// <summary>App executable path in the install directory.</summary>
    public static string AppExe { get; } = Path.Combine(AppInstall, "CopilotFamily.App.exe");

    /// <summary>Ensures all required directories exist.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(NexusInstall);
        Directory.CreateDirectory(AppInstall);
        Directory.CreateDirectory(StagingRoot);
        Directory.CreateDirectory(NexusStaging);
        Directory.CreateDirectory(AppStaging);
        Directory.CreateDirectory(Logs);
    }

    /// <summary>
    /// Returns the staging path for a given component.
    /// </summary>
    public static string GetStagingPath(string component) => component.ToLowerInvariant() switch
    {
        "nexus" => NexusStaging,
        "app" => AppStaging,
        _ => throw new ArgumentException($"Unknown component: {component}", nameof(component)),
    };

    /// <summary>
    /// Returns the install path for a given component.
    /// </summary>
    public static string GetInstallPath(string component) => component.ToLowerInvariant() switch
    {
        "nexus" => NexusInstall,
        "app" => AppInstall,
        _ => throw new ArgumentException($"Unknown component: {component}", nameof(component)),
    };
}
