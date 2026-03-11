using System.Diagnostics;
using System.Net.Http.Json;
using CopilotNexus.Core;
using Spectre.Console;

record HealthResponse(string? Status, int Sessions, int Models, string? Uptime);

/// <summary>
/// All CLI command implementations. The CLI is a separate process from the Service,
/// so commands like 'update' can safely overwrite Service files without self-locking.
/// </summary>
internal static class CliCommands
{
    // --- start ---
    internal static void RunStart(string url)
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
                    AnsiConsole.MarkupLine("Use [blue]nexus stop[/] first.");
                    return;
                }
                catch (ArgumentException)
                {
                    // Process not running — stale lock file, continue
                }
            }
        }

        // Find the Service executable
        var serviceExe = FindServiceExecutable();
        if (serviceExe == null)
        {
            AnsiConsole.MarkupLine("[red]Cannot find CopilotNexus.Service.exe[/]");
            AnsiConsole.MarkupLine($"  Expected at: [dim]{Markup.Escape(CopilotNexusPaths.ServiceExe)}[/]");
            AnsiConsole.MarkupLine("  Run [blue]nexus install[/] first.");
            Environment.ExitCode = 1;
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = serviceExe,
            Arguments = $"--urls {url}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var proc = Process.Start(psi);
        if (proc == null)
        {
            AnsiConsole.MarkupLine("[red]Failed to start Nexus service.[/]");
            Environment.ExitCode = 1;
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Nexus service started (PID [bold]{proc.Id}[/])");
        AnsiConsole.MarkupLine($"  URL: [link]{url}[/]");
        AnsiConsole.MarkupLine("  Use [blue]nexus status[/] to check · [blue]nexus stop[/] to stop");
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

        try { File.Delete(lockFile); } catch { }
    }

    // --- status ---
    internal static async Task RunStatusAsync(string url)
    {
        var baseUrl = url.TrimEnd('/');

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

        string serviceStatus;
        string sessions = "—", models = "—", uptime = "—";

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

        var nexusStaging = CopilotNexusPaths.NexusStaging;
        var appStaging = CopilotNexusPaths.AppStaging;
        var nexusHasUpdate = Directory.Exists(nexusStaging) && Directory.EnumerateFiles(nexusStaging).Any();
        var appHasUpdate = Directory.Exists(appStaging) && Directory.EnumerateFiles(appStaging).Any();

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
        table.AddEmptyRow();
        table.AddRow("Install dir", Markup.Escape(CopilotNexusPaths.Root));
        table.AddRow("Log dir", Markup.Escape(CopilotNexusPaths.Logs));

        AnsiConsole.Write(table);
    }

    // --- build ---
    internal static async Task RunBuildAsync(string configuration)
    {
        var repoRoot = FindRepoRoot();
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

        var repoRoot = FindRepoRoot();
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
            .StartAsync("Publishing components...", async ctx =>
            {
                ctx.Status("Publishing CLI...");
                await PublishComponent(repoRoot, "cli", CopilotNexusPaths.NexusInstall);
                ctx.Status("Publishing Service...");
                await PublishComponent(repoRoot, "service", CopilotNexusPaths.NexusInstall);
                ctx.Status("Publishing App...");
                await PublishComponent(repoRoot, "app", CopilotNexusPaths.AppInstall);
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ Installation complete.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Set up the [blue]nexus[/] alias for easy access:");
        AnsiConsole.MarkupLine($"  [dim]Set-Alias nexus \"{Markup.Escape(CopilotNexusPaths.CliExe)}\"[/]");
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
                    // Stop Nexus service if updating it
                    if (comp == "nexus" && File.Exists(CopilotNexusPaths.NexusLockFile))
                    {
                        ctx.Status("Stopping Nexus service for update...");
                        RunStop();
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

                    // Restart service if we updated nexus
                    if (comp == "nexus")
                    {
                        ctx.Status("Restarting Nexus service...");
                        var serviceExe = FindServiceExecutable();
                        if (serviceExe != null)
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = serviceExe,
                                Arguments = "--urls http://localhost:5280",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            };
                            Process.Start(psi);
                        }
                    }
                });

            AnsiConsole.MarkupLine($"[green]✓[/] {comp} updated successfully.");
        }
    }

    // --- publish ---
    internal static async Task RunPublishAsync(string component)
    {
        var nexusInstalled = File.Exists(CopilotNexusPaths.CliExe) || File.Exists(CopilotNexusPaths.ServiceExe);
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

        var repoRoot = FindRepoRoot();
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
                    var stagingPath = CopilotNexusPaths.GetStagingPath(comp);
                    if (comp == "nexus")
                    {
                        ctx.Status("Publishing CLI to staging...");
                        await PublishComponent(repoRoot, "cli", stagingPath);
                        ctx.Status("Publishing Service to staging...");
                        await PublishComponent(repoRoot, "service", stagingPath);
                    }
                    else
                    {
                        ctx.Status($"Publishing {comp} to staging...");
                        await PublishComponent(repoRoot, comp, stagingPath);
                    }
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

    private static string? FindServiceExecutable()
    {
        string[] searchPaths =
        [
            CopilotNexusPaths.ServiceExe,
            Path.Combine(AppContext.BaseDirectory, "CopilotNexus.Service.exe"),
        ];

        var repoRoot = FindRepoRoot();
        if (repoRoot != null)
        {
            searchPaths =
            [
                ..searchPaths,
                Path.Combine(repoRoot, "src", "CopilotNexus.Service", "bin", "Debug", "net8.0", "CopilotNexus.Service.exe"),
                Path.Combine(repoRoot, "src", "CopilotNexus.Service", "bin", "Release", "net8.0", "CopilotNexus.Service.exe"),
            ];
        }

        return searchPaths.FirstOrDefault(File.Exists);
    }

    private static string? FindAppExecutable()
    {
        string[] searchPaths =
        [
            CopilotNexusPaths.AppExe,
            Path.Combine(AppContext.BaseDirectory, "CopilotNexus.App.exe"),
        ];

        var repoRoot = FindRepoRoot();
        if (repoRoot != null)
        {
            searchPaths =
            [
                ..searchPaths,
                Path.Combine(repoRoot, "src", "CopilotNexus.App", "bin", "Debug", "net8.0", "CopilotNexus.App.exe"),
                Path.Combine(repoRoot, "src", "CopilotNexus.App", "bin", "Release", "net8.0", "CopilotNexus.App.exe"),
            ];
        }

        return searchPaths.FirstOrDefault(File.Exists);
    }

    private static string? FindRepoRoot()
    {
        return FindRepoRootFrom(AppContext.BaseDirectory) ?? FindRepoRootFrom(Environment.CurrentDirectory);
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

    private static async Task PublishComponent(string repoRoot, string component, string outputPath)
    {
        var projectPath = component switch
        {
            "cli" => Path.Combine(repoRoot, "src", "CopilotNexus.Cli", "CopilotNexus.Cli.csproj"),
            "service" => Path.Combine(repoRoot, "src", "CopilotNexus.Service", "CopilotNexus.Service.csproj"),
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
        CopyDirectory(source, destination);
    }

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
                return;
            }
            catch (IOException)
            {
                await Task.Delay(1000);
            }
        }
    }
}
