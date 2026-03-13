namespace CopilotNexus.Core;

/// <summary>
/// Centralized path definitions for the CopilotNexus installation layout.
/// Install/runtime binaries live under %LOCALAPPDATA%\CopilotNexus\app\{component}\.
/// Nexus-owned app state lives under %LOCALAPPDATA%\CopilotNexus\state\.
/// User profile state is retained as a local fallback for app test mode.
/// </summary>
public static class CopilotNexusPaths
{
    /// <summary>Root directory: %LOCALAPPDATA%\CopilotNexus\</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotNexus");

    /// <summary>Versioned runtime root containing cli/service/winapp shims and payloads.</summary>
    public static string AppRoot { get; } = Path.Combine(Root, "app");

    /// <summary>Nexus service shim/payload root.</summary>
    public static string NexusInstall { get; } = Path.Combine(AppRoot, "service");

    /// <summary>Nexus CLI shim/payload root.</summary>
    public static string CliInstall { get; } = Path.Combine(AppRoot, "cli");

    /// <summary>Desktop app shim/payload root.</summary>
    public static string AppInstall { get; } = Path.Combine(AppRoot, "winapp");

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

    /// <summary>Service-owned state root for persisted app metadata.</summary>
    public static string StateRoot { get; } = Path.Combine(Root, "state");

    /// <summary>PID lock file for the Nexus service process.</summary>
    public static string NexusLockFile { get; } = Path.Combine(Root, "nexus.lock");

    /// <summary>User-specific config root: %USERPROFILE%\.copilot-nexus\.</summary>
    public static string UserConfigRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot-nexus");

    /// <summary>Local fallback state file (used in app test mode).</summary>
    public static string AppStateFile { get; } = Path.Combine(UserConfigRoot, "session-state.json");

    /// <summary>Nexus-owned application state file used by service APIs.</summary>
    public static string NexusAppStateFile { get; } = Path.Combine(StateRoot, "session-state.json");

    /// <summary>Nexus-owned session profiles file used by service APIs.</summary>
    public static string NexusSessionProfilesFile { get; } = Path.Combine(StateRoot, "session-profiles.json");

    /// <summary>CLI publish version tracking state.</summary>
    public static string PublishVersionStateFile { get; } = Path.Combine(StateRoot, "publish-version-state.json");

    /// <summary>Service runtime agent selection state.</summary>
    public static string RuntimeAgentConfigFile { get; } = Path.Combine(StateRoot, "runtime-config.json");

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
        Directory.CreateDirectory(AppRoot);
        Directory.CreateDirectory(NexusInstall);
        Directory.CreateDirectory(CliInstall);
        Directory.CreateDirectory(AppInstall);
        Directory.CreateDirectory(StagingRoot);
        Directory.CreateDirectory(NexusStaging);
        Directory.CreateDirectory(CliStaging);
        Directory.CreateDirectory(AppStaging);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(StateRoot);
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

    /// <summary>
    /// Returns the versioned payload directory path for a component.
    /// </summary>
    public static string GetVersionedInstallPath(string component, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version is required.", nameof(version));
        return Path.Combine(GetInstallPath(component), version.Trim());
    }
}
