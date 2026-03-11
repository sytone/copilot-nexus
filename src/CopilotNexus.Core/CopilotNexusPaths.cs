namespace CopilotNexus.Core;

/// <summary>
/// Centralized path definitions for the CopilotNexus installation layout.
/// Install/runtime binaries live under %LOCALAPPDATA%\CopilotNexus\.
/// User-specific app configuration/state lives under %USERPROFILE%\.copilot-nexus\.
/// </summary>
public static class CopilotNexusPaths
{
    /// <summary>Root directory: %LOCALAPPDATA%\CopilotNexus\</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotNexus");

    /// <summary>Nexus service install directory.</summary>
    public static string NexusInstall { get; } = Path.Combine(Root, "nexus");

    /// <summary>Nexus CLI install directory (separate to avoid self-update locks).</summary>
    public static string CliInstall { get; } = Path.Combine(Root, "cli");

    /// <summary>Desktop app install directory.</summary>
    public static string AppInstall { get; } = Path.Combine(Root, "app");

    /// <summary>Shared staging root — updates are staged here before being applied.</summary>
    public static string StagingRoot { get; } = Path.Combine(Root, "staging");

    /// <summary>Staging directory for Nexus (CLI + Service) updates.</summary>
    public static string NexusStaging { get; } = Path.Combine(StagingRoot, "nexus");

    /// <summary>Staging directory for CLI updates when self-overwrite is not possible.</summary>
    public static string CliStaging { get; } = Path.Combine(StagingRoot, "cli");

    /// <summary>Staging directory for App updates.</summary>
    public static string AppStaging { get; } = Path.Combine(StagingRoot, "app");

    /// <summary>Shared log directory.</summary>
    public static string Logs { get; } = Path.Combine(Root, "logs");

    /// <summary>PID lock file for the Nexus service process.</summary>
    public static string NexusLockFile { get; } = Path.Combine(Root, "nexus.lock");

    /// <summary>User-specific config root: %USERPROFILE%\.copilot-nexus\.</summary>
    public static string UserConfigRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot-nexus");

    /// <summary>Application state file (tab layout, session IDs).</summary>
    public static string AppStateFile { get; } = Path.Combine(UserConfigRoot, "session-state.json");

    /// <summary>CLI executable — the 'nexus' command users interact with.</summary>
    public static string CliExe { get; } = Path.Combine(CliInstall, "CopilotNexus.Cli.exe");

    /// <summary>Service executable — the ASP.NET Core backend launched by the CLI.</summary>
    public static string ServiceExe { get; } = Path.Combine(NexusInstall, "CopilotNexus.Service.exe");

    /// <summary>App executable path in the install directory.</summary>
    public static string AppExe { get; } = Path.Combine(AppInstall, "CopilotNexus.App.exe");

    /// <summary>Updater shim executable path (lives alongside the app).</summary>
    public static string UpdaterExe { get; } = Path.Combine(AppInstall, "CopilotNexus.Updater.exe");

    /// <summary>Ensures all required directories exist.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(NexusInstall);
        Directory.CreateDirectory(CliInstall);
        Directory.CreateDirectory(AppInstall);
        Directory.CreateDirectory(StagingRoot);
        Directory.CreateDirectory(NexusStaging);
        Directory.CreateDirectory(CliStaging);
        Directory.CreateDirectory(AppStaging);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(UserConfigRoot);
    }

    /// <summary>
    /// Returns the staging path for a given component.
    /// </summary>
    public static string GetStagingPath(string component) => component.ToLowerInvariant() switch
    {
        "nexus" => NexusStaging,
        "cli" => CliStaging,
        "app" => AppStaging,
        _ => throw new ArgumentException($"Unknown component: {component}", nameof(component)),
    };

    /// <summary>
    /// Returns the install path for a given component.
    /// </summary>
    public static string GetInstallPath(string component) => component.ToLowerInvariant() switch
    {
        "nexus" => NexusInstall,
        "cli" => CliInstall,
        "app" => AppInstall,
        _ => throw new ArgumentException($"Unknown component: {component}", nameof(component)),
    };
}
