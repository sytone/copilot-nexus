using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Json;
using CopilotNexus.Core;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Service;
using Serilog;
using Spectre.Console;

// WebApplicationFactory needs a code path that builds a WebApplication host
// so HostFactoryResolver can intercept it. We set NEXUS_TEST_MODE in the
// test factory to signal this — all other invocations go through System.CommandLine.
if (Environment.GetEnvironmentVariable("NEXUS_TEST_MODE") == "1")
{
    var builder = NexusHostBuilder.CreateBuilder(args);
    var app = NexusHostBuilder.ConfigureApp(builder.Build());
    var mgr = app.Services.GetRequiredService<ISessionManager>();
    await mgr.InitializeAsync();
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

// --- nexus build ---
var buildConfigOption = new Option<string>("--configuration", "-c") { Description = "Build configuration", DefaultValueFactory = _ => "Release" };
var buildCommand = new Command("build", "Build the solution from the repository") { buildConfigOption };
buildCommand.SetAction(async (parseResult, _) =>
{
    var config = parseResult.GetValue(buildConfigOption)!;
    await Program.RunBuildAsync(config);
});

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

var winappStartCommand = new Command("start", "Launch the Copilot Nexus desktop application")
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
var rootCommand = new RootCommand("CopilotNexus Nexus — Copilot session management service");
rootCommand.Subcommands.Add(startCommand);
rootCommand.Subcommands.Add(stopCommand);
rootCommand.Subcommands.Add(statusCommand);
rootCommand.Subcommands.Add(buildCommand);
rootCommand.Subcommands.Add(installCommand);
rootCommand.Subcommands.Add(updateCommand);
rootCommand.Subcommands.Add(publishCommand);
rootCommand.Subcommands.Add(winappCommand);

return rootCommand.Parse(args).Invoke();

record HealthResponse(string? Status, int Sessions, int Models, string? Uptime);

/// <summary>Partial class with command implementations.</summary>
public partial class Program
{

    // --- start (background) ---
    internal static void RunStartBackground(string url)
    {
        // Check if already running
        var lockFile = CopilotNexusPaths.NexusLockFile;
        if (File.Exists(lockFile))
        {
            var pidText = File.ReadAllText(lockFile).Trim();
            if (int.TryParse(pidText, out var existingPid))
            {
                try
                {
                    Process.GetProcessById(existingPid);
                    AnsiConsole.MarkupLine($"[yellow]Nexus is already running[/] (PID [bold]{existingPid}[/]).");
                    AnsiConsole.MarkupLine("Use [blue]nexus stop[/] first, or [blue]nexus start --interactive[/] to run in foreground.");
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
            AnsiConsole.MarkupLine("[red]Cannot determine executable path.[/] Use [blue]nexus start --interactive[/] instead.");
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
            AnsiConsole.MarkupLine("[red]Failed to start Nexus background process.[/]");
            Environment.ExitCode = 1;
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Nexus started in background (PID [bold]{proc.Id}[/])");
        AnsiConsole.MarkupLine($"  URL: [link]{url}[/]");
        AnsiConsole.MarkupLine("  Use [blue]nexus status[/] to check · [blue]nexus stop[/] to stop");
    }

    // --- start (interactive/foreground) ---
    internal static async Task RunServerAsync(string url, CancellationToken ct)
    {
        CopilotNexusPaths.EnsureDirectories();
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
        var lockFile = CopilotNexusPaths.NexusLockFile;
        if (!File.Exists(lockFile))
        {
            AnsiConsole.MarkupLine("[yellow]Nexus is not running[/] (no lock file found)");
            Environment.ExitCode = 1;
            return;
        }

        var pidText = File.ReadAllText(lockFile).Trim();
        if (!int.TryParse(pidText, out var pid))
        {
            AnsiConsole.MarkupLine($"[red]Invalid PID in lock file:[/] {Markup.Escape(pidText)}");
            File.Delete(lockFile);
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            var proc = Process.GetProcessById(pid);
            AnsiConsole.MarkupLine($"Stopping Nexus (PID [bold]{pid}[/])...");
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(10_000);
            AnsiConsole.MarkupLine("[green]✓[/] Nexus stopped.");
        }
        catch (ArgumentException)
        {
            AnsiConsole.MarkupLine($"[yellow]Nexus process (PID {pid}) is not running.[/] Cleaning up lock file.");
        }

        DeleteLockFile();
    }

    // --- status ---
    internal static async Task RunStatusAsync(string url)
    {
        var baseUrl = url.TrimEnd('/');

        // Gather process info
        string processStatus;
        string pidDisplay;
        var lockFile = CopilotNexusPaths.NexusLockFile;
        if (File.Exists(lockFile))
        {
            var pidText = File.ReadAllText(lockFile).Trim();
            if (int.TryParse(pidText, out var pid))
            {
                try
                {
                    Process.GetProcessById(pid);
                    processStatus = "[green]Running[/]";
                    pidDisplay = pid.ToString();
                }
                catch (ArgumentException)
                {
                    processStatus = "[yellow]Stale lock file[/]";
                    pidDisplay = $"{pid} (dead)";
                }
            }
            else
            {
                processStatus = "[red]Invalid lock file[/]";
                pidDisplay = Markup.Escape(pidText);
            }
        }
        else
        {
            processStatus = "[grey]Not running[/]";
            pidDisplay = "—";
        }

        // Query health endpoint
        string serviceStatus;
        string sessions = "—";
        string models = "—";
        string uptime = "—";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await http.GetAsync($"{baseUrl}/health");
            response.EnsureSuccessStatusCode();

            var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
            serviceStatus = "[green]Responding[/]";
            sessions = (health?.Sessions ?? 0).ToString();
            models = (health?.Models ?? 0).ToString();
            uptime = health?.Uptime ?? "—";
        }
        catch (HttpRequestException)
        {
            serviceStatus = "[red]Not responding[/]";
            Environment.ExitCode = 1;
        }
        catch (TaskCanceledException)
        {
            serviceStatus = "[red]Timeout[/]";
            Environment.ExitCode = 1;
        }

        // Check staging
        var nexusStaging = CopilotNexusPaths.NexusStaging;
        var appStaging = CopilotNexusPaths.AppStaging;
        var nexusHasUpdate = Directory.Exists(nexusStaging) && Directory.EnumerateFiles(nexusStaging).Any();
        var appHasUpdate = Directory.Exists(appStaging) && Directory.EnumerateFiles(appStaging).Any();

        // Render status table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Nexus Status[/]")
            .AddColumn(new TableColumn("[bold]Property[/]").Width(22))
            .AddColumn(new TableColumn("[bold]Value[/]"));

        table.AddRow("Process", processStatus);
        table.AddRow("PID", pidDisplay);
        table.AddRow("URL", Markup.Escape(baseUrl));
        table.AddRow("Health", serviceStatus);
        table.AddRow("Sessions", sessions);
        table.AddRow("Models", models);
        table.AddRow("Started", Markup.Escape(uptime));

        table.AddEmptyRow();
        table.AddRow("Nexus update staged", nexusHasUpdate ? "[green]Yes[/]" : "[grey]No[/]");
        table.AddRow("App update staged", appHasUpdate ? "[green]Yes[/]" : "[grey]No[/]");

        // Paths
        table.AddEmptyRow();
        table.AddRow("Install dir", Markup.Escape(CopilotNexusPaths.Root));
        table.AddRow("Log dir", Markup.Escape(CopilotNexusPaths.Logs));

        AnsiConsole.Write(table);
    }

    // --- build ---
    internal static async Task RunBuildAsync(string configuration)
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            AnsiConsole.MarkupLine("[red]Cannot find repository root.[/] Run from within the repo.");
            Environment.ExitCode = 1;
            return;
        }

        var slnPath = Path.Combine(repoRoot, "CopilotNexus.slnx");

        var rule = new Rule($"[bold blue]Building CopilotNexus[/] [dim]({configuration})[/]").LeftJustified();
        AnsiConsole.Write(rule);
        AnsiConsole.MarkupLine($"  Solution: [dim]{Markup.Escape(slnPath)}[/]");
        AnsiConsole.WriteLine();

        var sw = Stopwatch.StartNew();

        var exitCode = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Building...", async _ =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{slnPath}\" -c {configuration} --nologo -v q",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = repoRoot,
                };

                var proc = Process.Start(psi)!;
                var stdout = await proc.StandardOutput.ReadToEndAsync();
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (proc.ExitCode != 0)
                {
                    AnsiConsole.MarkupLine("[red]✗ Build failed[/]");
                    AnsiConsole.WriteLine();
                    if (!string.IsNullOrWhiteSpace(stderr))
                        AnsiConsole.MarkupLine(Markup.Escape(stderr.Trim()));
                    if (!string.IsNullOrWhiteSpace(stdout))
                        AnsiConsole.MarkupLine(Markup.Escape(stdout.Trim()));
                }

                return proc.ExitCode;
            });

        sw.Stop();

        if (exitCode == 0)
        {
            AnsiConsole.MarkupLine($"[green]✓ Build succeeded[/] in [bold]{sw.Elapsed.TotalSeconds:F1}s[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Next: [blue]nexus publish[/] to stage the update.");
        }
        else
        {
            Environment.ExitCode = 1;
        }
    }

    // --- install ---
    internal static async Task RunInstallAsync()
    {
        CopilotNexusPaths.EnsureDirectories();

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            AnsiConsole.MarkupLine("[red]Cannot find repository root.[/] Run from within the repo.");
            Environment.ExitCode = 1;
            return;
        }

        var rule = new Rule("[bold blue]Installing CopilotNexus[/]").LeftJustified();
        AnsiConsole.Write(rule);
        AnsiConsole.MarkupLine($"  Repo:    [dim]{Markup.Escape(repoRoot)}[/]");
        AnsiConsole.MarkupLine($"  Install: [dim]{Markup.Escape(CopilotNexusPaths.Root)}[/]");
        AnsiConsole.WriteLine();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Publishing Nexus...", async ctx =>
            {
                await PublishComponent(repoRoot, "nexus", CopilotNexusPaths.NexusInstall);
                ctx.Status("Publishing App...");
                await PublishComponent(repoRoot, "app", CopilotNexusPaths.AppInstall);
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ Installation complete.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Set up the [blue]nexus[/] alias for easy access:");
        AnsiConsole.MarkupLine($"  [dim]Set-Alias nexus \"{Markup.Escape(CopilotNexusPaths.NexusExe)}\"[/]");
    }

    // --- update ---
    internal static async Task RunUpdateAsync(string component)
    {
        var components = component == "both" ? new[] { "nexus", "app" } : new[] { component };

        foreach (var comp in components)
        {
            var stagingPath = CopilotNexusPaths.GetStagingPath(comp);
            var installPath = CopilotNexusPaths.GetInstallPath(comp);

            if (!Directory.Exists(stagingPath) || !Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories).Any())
            {
                AnsiConsole.MarkupLine($"[grey]No staged update for {comp}.[/]");
                continue;
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("blue"))
                .StartAsync($"Updating {comp}...", async ctx =>
                {
                    // Stop Nexus if updating it
                    if (comp == "nexus" && File.Exists(CopilotNexusPaths.NexusLockFile))
                    {
                        ctx.Status("Stopping Nexus for update...");
                        RunStop();
                        // Wait for process to fully exit and release file locks
                        await WaitForFileLockRelease(installPath, 15);
                    }

                    // Copy staging to install with retry for lingering locks
                    ctx.Status($"Copying {comp} files...");
                    await CopyDirectoryWithRetryAsync(stagingPath, installPath);

                    // Clear staging
                    ctx.Status("Clearing staging...");
                    foreach (var file in Directory.GetFiles(stagingPath, "*", SearchOption.AllDirectories))
                        File.Delete(file);
                    foreach (var dir in Directory.GetDirectories(stagingPath).Reverse())
                        Directory.Delete(dir, true);

                    // Restart Nexus if we updated it
                    if (comp == "nexus")
                    {
                        ctx.Status("Restarting Nexus...");
                        var psi = new ProcessStartInfo
                        {
                            FileName = CopilotNexusPaths.NexusExe,
                            Arguments = "start",
                            UseShellExecute = false,
                        };
                        Process.Start(psi);
                    }
                });

            AnsiConsole.MarkupLine($"[green]✓[/] {comp} updated successfully.");
        }
    }

    // --- publish ---
    internal static async Task RunPublishAsync(string component)
    {
        // Check if the app has been installed
        var nexusInstalled = File.Exists(CopilotNexusPaths.NexusExe);
        var appInstalled = File.Exists(CopilotNexusPaths.AppExe);

        if (!nexusInstalled && !appInstalled)
        {
            AnsiConsole.MarkupLine("[red]CopilotNexus is not installed.[/]");
            AnsiConsole.MarkupLine($"  Expected install at: [dim]{Markup.Escape(CopilotNexusPaths.Root)}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Run [blue]nexus install[/] first to perform the initial installation.");
            Environment.ExitCode = 1;
            return;
        }

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            AnsiConsole.MarkupLine("[red]Cannot find repository root.[/] Run from within the repo.");
            Environment.ExitCode = 1;
            return;
        }

        CopilotNexusPaths.EnsureDirectories();

        var components = component == "both" ? new[] { "nexus", "app" } : new[] { component };

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Publishing to staging...", async ctx =>
            {
                foreach (var comp in components)
                {
                    ctx.Status($"Publishing {comp} to staging...");
                    var stagingPath = CopilotNexusPaths.GetStagingPath(comp);
                    await PublishComponent(repoRoot, comp, stagingPath);
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ Staged updates ready.[/]");
        AnsiConsole.WriteLine();

        var panel = new Panel(
            "  [blue]nexus update[/]                      apply all staged updates\n" +
            "  [blue]nexus update --component nexus[/]    apply Nexus update only\n" +
            "  [blue]nexus update --component app[/]      apply App update only\n\n" +
            "  [dim]The desktop app also detects staged app updates automatically.[/]")
            .Header("[bold]Next steps[/]")
            .Border(BoxBorder.Rounded)
            .Padding(1, 0);

        AnsiConsole.Write(panel);
    }

    // --- winapp start ---
    internal static void RunWinApp(string nexusUrl, bool testMode)
    {
        var appExe = FindAppExecutable();

        if (appExe == null)
        {
            AnsiConsole.MarkupLine("[red]Could not find CopilotNexus.App.exe[/]");
            AnsiConsole.MarkupLine($"  Install dir: [dim]{Markup.Escape(CopilotNexusPaths.AppInstall)}[/]");
            Environment.ExitCode = 1;
            return;
        }

        var arguments = $"--nexus-url {nexusUrl}";
        if (testMode) arguments += " --test-mode";

        var psi = new ProcessStartInfo
        {
            FileName = appExe,
            Arguments = arguments,
            UseShellExecute = false,
        };

        var process = Process.Start(psi);
        if (process != null)
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Launched [bold]{Markup.Escape(Path.GetFileName(appExe))}[/] (PID [bold]{process.Id}[/])");
            AnsiConsole.MarkupLine($"  Nexus URL: [link]{nexusUrl}[/]");
            if (testMode) AnsiConsole.MarkupLine("  Mode: [yellow]test[/]");
        }
    }

    // --- Helpers ---

    private static void WriteLockFile()
    {
        File.WriteAllText(CopilotNexusPaths.NexusLockFile, Environment.ProcessId.ToString());
    }

    private static void DeleteLockFile()
    {
        try { File.Delete(CopilotNexusPaths.NexusLockFile); } catch { /* best effort */ }
    }

    private static async Task PublishComponent(string repoRoot, string component, string outputPath)
    {
        var projectPath = component switch
        {
            "nexus" => Path.Combine(repoRoot, "src", "CopilotNexus.Service", "CopilotNexus.Service.csproj"),
            "app" => Path.Combine(repoRoot, "src", "CopilotNexus.App", "CopilotNexus.App.csproj"),
            _ => throw new ArgumentException($"Unknown component: {component}"),
        };

        AnsiConsole.MarkupLine($"  [blue]{component}[/] → [dim]{Markup.Escape(outputPath)}[/]");

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
            AnsiConsole.MarkupLine($"  [red]✗ Failed to publish {component}[/]");
            AnsiConsole.WriteException(new InvalidOperationException(stderr.Trim()));
            Environment.ExitCode = 1;
        }
        else
        {
            AnsiConsole.MarkupLine($"  [green]✓[/] {component} published");
        }
    }

    private static string? FindAppExecutable()
    {
        string[] searchPaths =
        [
            CopilotNexusPaths.AppExe,
            Path.Combine(AppContext.BaseDirectory, "CopilotNexus.App.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "app", "CopilotNexus.App.exe"),
        ];

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot != null)
        {
            searchPaths =
            [
                ..searchPaths,
                Path.Combine(repoRoot, "dist", "CopilotNexus.App.exe"),
                Path.Combine(repoRoot, "dist", "staging", "CopilotNexus.App.exe"),
            ];
        }

        return searchPaths.FirstOrDefault(File.Exists);
    }

    private static string? FindRepoRoot(string startDir)
    {
        // Search from startDir (AppContext.BaseDirectory) first,
        // then fall back to the current working directory.
        return FindRepoRootFrom(startDir) ?? FindRepoRootFrom(Environment.CurrentDirectory);
    }

    internal static string? FindRepoRootFrom(string startDir)
    {
        var dir = startDir;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) ||
                File.Exists(Path.Combine(dir, "CopilotNexus.slnx")))
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

    /// <summary>
    /// Copies a directory with retry logic for files locked by a recently-exited process.
    /// </summary>
    private static async Task CopyDirectoryWithRetryAsync(string source, string destination, int maxRetries = 10)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                CopyDirectory(source, destination);
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(1000);
            }
        }

        // Final attempt — let the exception propagate
        CopyDirectory(source, destination);
    }

    /// <summary>
    /// Waits until files in the target directory are no longer locked.
    /// </summary>
    private static async Task WaitForFileLockRelease(string directory, int timeoutSeconds)
    {
        if (!Directory.Exists(directory)) return;

        var testFile = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (testFile == null) return;

        for (int i = 0; i < timeoutSeconds; i++)
        {
            try
            {
                using var fs = File.Open(testFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return; // File is unlocked
            }
            catch (IOException)
            {
                await Task.Delay(1000);
            }
        }
    }
}
