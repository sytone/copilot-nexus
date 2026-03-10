using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Json;
using CopilotFamily.Core;
using CopilotFamily.Core.Interfaces;
using CopilotFamily.Nexus;
using Serilog;

// When invoked with no args or via WebApplicationFactory, start the server directly.
// WebApplicationFactory intercepts WebApplication.CreateBuilder(args) to capture the host.
if (args.Length == 0 || !Program.IsCliCommand(args))
{
    var builder = NexusHostBuilder.CreateBuilder(args);
    var app = NexusHostBuilder.ConfigureApp(builder.Build());
    var mgr = app.Services.GetRequiredService<ISessionManager>();
    await mgr.InitializeAsync();
    Log.Information("Nexus service starting");
    await app.RunAsync();
    return 0;
}

// CLI command routing
var urlOption = new Option<string>("--url") { Description = "URL to listen on", DefaultValueFactory = _ => "http://localhost:5280" };
var interactiveOption = new Option<bool>("--interactive") { Description = "Run in the current process instead of as a background service" };

// --- nexus start ---
var startCommand = new Command("start", "Start the Nexus service (background by default)") { urlOption, interactiveOption };
startCommand.SetAction(async (parseResult, ct) =>
{
    var url = parseResult.GetValue(urlOption)!;
    var interactive = parseResult.GetValue(interactiveOption);

    if (interactive)
    {
        await Program.RunServerAsync(url, ct);
    }
    else
    {
        Program.RunStartBackground(url);
    }
});

// --- nexus stop ---
var stopCommand = new Command("stop", "Stop a running Nexus instance");
stopCommand.SetAction((_, _) => { Program.RunStop(); return Task.CompletedTask; });

// --- nexus status ---
var statusUrlOption = new Option<string>("--url") { Description = "Nexus URL to query", DefaultValueFactory = _ => "http://localhost:5280" };
var statusCommand = new Command("status", "Check the status of a running Nexus instance") { statusUrlOption };
statusCommand.SetAction(async (parseResult, _) =>
{
    var url = parseResult.GetValue(statusUrlOption)!;
    await Program.RunStatusAsync(url);
});

// --- nexus install ---
var installCommand = new Command("install", "Install Nexus and App to the local install directory");
installCommand.SetAction(async (_, _) => { await Program.RunInstallAsync(); });

// --- nexus update ---
var updateComponentOption = new Option<string>("--component") { Description = "Component to update (nexus, app, or both)", DefaultValueFactory = _ => "both" };
var updateCommand = new Command("update", "Update a component from staging") { updateComponentOption };
updateCommand.SetAction(async (parseResult, _) =>
{
    var component = parseResult.GetValue(updateComponentOption)!;
    await Program.RunUpdateAsync(component);
});

// --- nexus publish ---
var publishComponentOption = new Option<string>("--component") { Description = "Component to publish (nexus, app, or both)", DefaultValueFactory = _ => "both" };
var publishCommand = new Command("publish", "Build and publish components to the install directory") { publishComponentOption };
publishCommand.SetAction(async (parseResult, _) =>
{
    var component = parseResult.GetValue(publishComponentOption)!;
    await Program.RunPublishAsync(component);
});

// --- nexus winapp ---
var nexusUrlOption = new Option<string>("--nexus-url") { Description = "Nexus URL for the app to connect to", DefaultValueFactory = _ => "http://localhost:5280" };
var testModeOption = new Option<bool>("--test-mode") { Description = "Run in test mode with mock services" };

var winappStartCommand = new Command("start", "Launch the Copilot Family desktop application")
{
    nexusUrlOption, testModeOption
};
winappStartCommand.SetAction((parseResult, _) =>
{
    var nexusUrl = parseResult.GetValue(nexusUrlOption)!;
    var testMode = parseResult.GetValue(testModeOption);
    Program.RunWinApp(nexusUrl, testMode);
    return Task.CompletedTask;
});

var winappCommand = new Command("winapp", "Manage the desktop application");
winappCommand.Subcommands.Add(winappStartCommand);

// --- root ---
var rootCommand = new RootCommand("CopilotFamily Nexus — Copilot session management service");
rootCommand.Subcommands.Add(startCommand);
rootCommand.Subcommands.Add(stopCommand);
rootCommand.Subcommands.Add(statusCommand);
rootCommand.Subcommands.Add(installCommand);
rootCommand.Subcommands.Add(updateCommand);
rootCommand.Subcommands.Add(publishCommand);
rootCommand.Subcommands.Add(winappCommand);

return rootCommand.Parse(args).Invoke();

record HealthResponse(string? Status, int Sessions, int Models, string? Uptime);

/// <summary>Partial class with command implementations.</summary>
public partial class Program
{
    internal static bool IsCliCommand(string[] args)
    {
        if (args.Length == 0) return false;
        string[] commands = ["start", "stop", "status", "install", "update", "publish", "winapp", "--help", "-h", "-?"];
        return commands.Contains(args[0], StringComparer.OrdinalIgnoreCase);
    }

    // --- start (background) ---
    internal static void RunStartBackground(string url)
    {
        // Check if already running
        var lockFile = CopilotFamilyPaths.NexusLockFile;
        if (File.Exists(lockFile))
        {
            var pidText = File.ReadAllText(lockFile).Trim();
            if (int.TryParse(pidText, out var existingPid))
            {
                try
                {
                    Process.GetProcessById(existingPid);
                    Console.WriteLine($"Nexus is already running (PID {existingPid}).");
                    Console.WriteLine("Use 'nexus stop' first, or 'nexus start --interactive' to run in foreground.");
                    return;
                }
                catch (ArgumentException)
                {
                    // Process not running — stale lock file, continue
                }
            }
        }

        // Find the current executable to relaunch with --interactive
        var exePath = Environment.ProcessPath;
        if (exePath == null)
        {
            Console.Error.WriteLine("Cannot determine executable path. Use 'nexus start --interactive' instead.");
            Environment.ExitCode = 1;
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"start --interactive --url {url}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        var proc = Process.Start(psi);
        if (proc == null)
        {
            Console.Error.WriteLine("Failed to start Nexus background process.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Nexus started in background (PID {proc.Id}).");
        Console.WriteLine($"  URL: {url}");
        Console.WriteLine("Use 'nexus status' to check or 'nexus stop' to stop.");
    }

    // --- start (interactive/foreground) ---
    internal static async Task RunServerAsync(string url, CancellationToken ct)
    {
        CopilotFamilyPaths.EnsureDirectories();
        WriteLockFile();

        var builder = NexusHostBuilder.CreateBuilder();
        builder.WebHost.UseUrls(url);

        var app = NexusHostBuilder.ConfigureApp(builder.Build());
        var sessionManager = app.Services.GetRequiredService<ISessionManager>();
        await sessionManager.InitializeAsync(ct);

        Log.Information("Nexus service starting on {Url} (PID {Pid})", url, Environment.ProcessId);

        try
        {
            await app.RunAsync(ct);
        }
        finally
        {
            DeleteLockFile();
        }
    }

    // --- stop ---
    internal static void RunStop()
    {
        var lockFile = CopilotFamilyPaths.NexusLockFile;
        if (!File.Exists(lockFile))
        {
            Console.Error.WriteLine("Nexus is not running (no lock file found)");
            Environment.ExitCode = 1;
            return;
        }

        var pidText = File.ReadAllText(lockFile).Trim();
        if (!int.TryParse(pidText, out var pid))
        {
            Console.Error.WriteLine($"Invalid PID in lock file: {pidText}");
            File.Delete(lockFile);
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            var proc = Process.GetProcessById(pid);
            Console.WriteLine($"Stopping Nexus (PID {pid})...");
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(10_000);
            Console.WriteLine("Nexus stopped.");
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"Nexus process (PID {pid}) is not running. Cleaning up lock file.");
        }

        DeleteLockFile();
    }

    // --- status ---
    internal static async Task RunStatusAsync(string url)
    {
        var baseUrl = url.TrimEnd('/');

        // Check lock file for PID
        var lockFile = CopilotFamilyPaths.NexusLockFile;
        if (File.Exists(lockFile))
        {
            var pidText = File.ReadAllText(lockFile).Trim();
            Console.WriteLine($"Lock file PID: {pidText}");
        }
        else
        {
            Console.WriteLine("Lock file: not found");
        }

        // Check health
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await http.GetAsync($"{baseUrl}/health");
            response.EnsureSuccessStatusCode();

            var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
            Console.WriteLine($"Nexus Status: {health?.Status ?? "Unknown"}");
            Console.WriteLine($"  URL:       {baseUrl}");
            Console.WriteLine($"  Sessions:  {health?.Sessions ?? 0}");
            Console.WriteLine($"  Models:    {health?.Models ?? 0}");
            Console.WriteLine($"  Timestamp: {health?.Uptime ?? "N/A"}");
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine($"Nexus is not responding at {baseUrl}");
            Environment.ExitCode = 1;
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine($"Nexus at {baseUrl} did not respond (timeout)");
            Environment.ExitCode = 1;
        }

        // Check staging
        var nexusStaging = CopilotFamilyPaths.NexusStaging;
        var appStaging = CopilotFamilyPaths.AppStaging;
        var nexusHasUpdate = Directory.Exists(nexusStaging) && Directory.EnumerateFiles(nexusStaging).Any();
        var appHasUpdate = Directory.Exists(appStaging) && Directory.EnumerateFiles(appStaging).Any();
        Console.WriteLine($"  Nexus update staged: {(nexusHasUpdate ? "yes" : "no")}");
        Console.WriteLine($"  App update staged:   {(appHasUpdate ? "yes" : "no")}");
    }

    // --- install ---
    internal static async Task RunInstallAsync()
    {
        CopilotFamilyPaths.EnsureDirectories();

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            Console.Error.WriteLine("Cannot find repository root. Run from within the repo.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine("Installing CopilotFamily...");
        Console.WriteLine($"  Repo:    {repoRoot}");
        Console.WriteLine($"  Install: {CopilotFamilyPaths.Root}");

        await PublishComponent(repoRoot, "nexus", CopilotFamilyPaths.NexusInstall);
        await PublishComponent(repoRoot, "app", CopilotFamilyPaths.AppInstall);

        Console.WriteLine("Installation complete.");
    }

    // --- update ---
    internal static async Task RunUpdateAsync(string component)
    {
        var components = component == "both" ? new[] { "nexus", "app" } : new[] { component };

        foreach (var comp in components)
        {
            var stagingPath = CopilotFamilyPaths.GetStagingPath(comp);
            var installPath = CopilotFamilyPaths.GetInstallPath(comp);

            if (!Directory.Exists(stagingPath) || !Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories).Any())
            {
                Console.WriteLine($"No staged update for {comp}.");
                continue;
            }

            Console.WriteLine($"Updating {comp}...");

            // Stop Nexus if updating it
            if (comp == "nexus" && File.Exists(CopilotFamilyPaths.NexusLockFile))
            {
                Console.WriteLine("  Stopping Nexus for update...");
                RunStop();
                await Task.Delay(2000); // Wait for process to fully exit
            }

            // Copy staging to install
            CopyDirectory(stagingPath, installPath);
            Console.WriteLine($"  Copied staging → {installPath}");

            // Clear staging
            foreach (var file in Directory.GetFiles(stagingPath, "*", SearchOption.AllDirectories))
                File.Delete(file);
            foreach (var dir in Directory.GetDirectories(stagingPath).Reverse())
                Directory.Delete(dir, true);
            Console.WriteLine("  Staging cleared.");

            // Restart Nexus if we updated it
            if (comp == "nexus")
            {
                Console.WriteLine("  Restarting Nexus...");
                var psi = new ProcessStartInfo
                {
                    FileName = CopilotFamilyPaths.NexusExe,
                    Arguments = "start",
                    UseShellExecute = false,
                };
                Process.Start(psi);
                Console.WriteLine("  Nexus restarted.");
            }
        }
    }

    // --- publish ---
    internal static async Task RunPublishAsync(string component)
    {
        // Check if the app has been installed
        var nexusInstalled = File.Exists(CopilotFamilyPaths.NexusExe);
        var appInstalled = File.Exists(CopilotFamilyPaths.AppExe);

        if (!nexusInstalled && !appInstalled)
        {
            Console.Error.WriteLine("CopilotFamily is not installed.");
            Console.Error.WriteLine($"  Expected install at: {CopilotFamilyPaths.Root}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Run 'nexus install' first to perform the initial installation.");
            Environment.ExitCode = 1;
            return;
        }

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            Console.Error.WriteLine("Cannot find repository root. Run from within the repo.");
            Environment.ExitCode = 1;
            return;
        }

        CopilotFamilyPaths.EnsureDirectories();

        var components = component == "both" ? new[] { "nexus", "app" } : new[] { component };

        Console.WriteLine("Publishing to staging...");
        foreach (var comp in components)
        {
            var stagingPath = CopilotFamilyPaths.GetStagingPath(comp);
            await PublishComponent(repoRoot, comp, stagingPath);
        }

        Console.WriteLine();
        Console.WriteLine("Staged updates ready. To apply:");
        Console.WriteLine("  nexus update              — apply all staged updates");
        Console.WriteLine("  nexus update --component nexus  — apply Nexus update only");
        Console.WriteLine("  nexus update --component app    — apply App update only");
        Console.WriteLine();
        Console.WriteLine("The desktop app will also detect staged app updates automatically.");
    }

    // --- winapp start ---
    internal static void RunWinApp(string nexusUrl, bool testMode)
    {
        // Search in: install dir, sibling dir, repo dist
        var appExe = FindAppExecutable();

        if (appExe == null)
        {
            Console.Error.WriteLine("Could not find CopilotFamily.App.exe");
            Console.Error.WriteLine($"  Install dir: {CopilotFamilyPaths.AppInstall}");
            Environment.ExitCode = 1;
            return;
        }

        var arguments = $"--nexus-url {nexusUrl}";
        if (testMode) arguments += " --test-mode";

        Console.WriteLine($"Launching {Path.GetFileName(appExe)}...");
        Console.WriteLine($"  Nexus URL: {nexusUrl}");
        if (testMode) Console.WriteLine("  Mode: test");

        var psi = new ProcessStartInfo
        {
            FileName = appExe,
            Arguments = arguments,
            UseShellExecute = false,
        };

        var process = Process.Start(psi);
        if (process != null)
        {
            Console.WriteLine($"  PID: {process.Id}");
        }
    }

    // --- Helpers ---

    private static void WriteLockFile()
    {
        File.WriteAllText(CopilotFamilyPaths.NexusLockFile, Environment.ProcessId.ToString());
    }

    private static void DeleteLockFile()
    {
        try { File.Delete(CopilotFamilyPaths.NexusLockFile); } catch { /* best effort */ }
    }

    private static async Task PublishComponent(string repoRoot, string component, string outputPath)
    {
        var projectPath = component switch
        {
            "nexus" => Path.Combine(repoRoot, "src", "CopilotFamily.Nexus", "CopilotFamily.Nexus.csproj"),
            "app" => Path.Combine(repoRoot, "src", "CopilotFamily.App", "CopilotFamily.App.csproj"),
            _ => throw new ArgumentException($"Unknown component: {component}"),
        };

        Console.WriteLine($"  Publishing {component} → {outputPath}");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectPath}\" -c Release -o \"{outputPath}\" --self-contained false --nologo -v q",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var proc = Process.Start(psi)!;
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine($"  Failed to publish {component}:");
            Console.Error.WriteLine(stderr);
            Environment.ExitCode = 1;
        }
        else
        {
            Console.WriteLine($"  {component} published successfully.");
        }
    }

    private static string? FindAppExecutable()
    {
        string[] searchPaths =
        [
            CopilotFamilyPaths.AppExe,
            Path.Combine(AppContext.BaseDirectory, "CopilotFamily.App.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "app", "CopilotFamily.App.exe"),
        ];

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot != null)
        {
            searchPaths =
            [
                ..searchPaths,
                Path.Combine(repoRoot, "dist", "CopilotFamily.App.exe"),
                Path.Combine(repoRoot, "dist", "staging", "CopilotFamily.App.exe"),
            ];
        }

        return searchPaths.FirstOrDefault(File.Exists);
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) ||
                File.Exists(Path.Combine(dir, "CopilotFamily.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }
}
