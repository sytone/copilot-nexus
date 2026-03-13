using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CopilotNexus.Core;
using Spectre.Console;

record HealthResponse(string? Status, int Sessions, int Models, string? Uptime);

/// <summary>
/// All CLI command implementations. CLI binaries are installed separately from the
/// Nexus service binaries to reduce self-update lock contention.
/// </summary>
internal static class CliCommands
{
    private const string PublishVersionBase = "0.1.0";
    private const string PublishVersionChannel = "dev";
    private sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Elapsed);
    private sealed record StartValidationResult(bool IsReady, string Message);

    internal static string GetCurrentVersion()
    {
        var assembly = typeof(CliCommands).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return !string.IsNullOrWhiteSpace(informational)
            ? informational
            : assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    internal static void PrintVersionBanner()
    {
        AnsiConsole.MarkupLine($"[dim]Copilot Nexus CLI {Markup.Escape(GetCurrentVersion())}[/]");
    }

    internal static void RunVersion()
    {
        var current = GetCurrentVersion();
        AnsiConsole.MarkupLine($"[bold]Copilot Nexus[/] [dim]{Markup.Escape(current)}[/]");
        AnsiConsole.MarkupLine($"  CLI: [dim]{Markup.Escape(GetExecutableVersion(CopilotNexusPaths.CliExe) ?? "not installed")}[/]");
        AnsiConsole.MarkupLine($"  Service: [dim]{Markup.Escape(GetExecutableVersion(CopilotNexusPaths.ServiceExe) ?? "not installed")}[/]");
        AnsiConsole.MarkupLine($"  App: [dim]{Markup.Escape(GetExecutableVersion(CopilotNexusPaths.AppExe) ?? "not installed")}[/]");
    }

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

        var validation = ValidateStartedServiceAsync(url, proc).GetAwaiter().GetResult();
        if (!validation.IsReady)
        {
            TryStopProcess(proc);
            AnsiConsole.MarkupLine("[red]Nexus failed start validation.[/]");
            AnsiConsole.MarkupLine($"  {Markup.Escape(validation.Message)}");
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
        CopilotNexusPaths.EnsureDirectories();

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

                WriteCliLog($"build start: {psi.FileName} {psi.Arguments}");
                var result = await RunProcessWithCapturedOutputAsync(psi);
                WriteCliLog(
                    $"build complete: exit={result.ExitCode}, elapsed={result.Elapsed.TotalSeconds:F1}s, " +
                    $"stdoutChars={result.StandardOutput.Length}, stderrChars={result.StandardError.Length}");

                if (result.ExitCode != 0)
                {
                    AnsiConsole.MarkupLine("[red]✗ Build failed[/]");
                    AnsiConsole.WriteLine();
                    if (!string.IsNullOrWhiteSpace(result.StandardError))
                        AnsiConsole.MarkupLine(Markup.Escape(result.StandardError.Trim()));
                    if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                        AnsiConsole.MarkupLine(Markup.Escape(result.StandardOutput.Trim()));
                }

                return result.ExitCode;
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
                await PublishComponent(repoRoot, "cli", CopilotNexusPaths.CliInstall);
                ctx.Status("Publishing Service...");
                await PublishComponent(repoRoot, "service", CopilotNexusPaths.NexusInstall);
                ctx.Status("Publishing App...");
                await PublishComponent(repoRoot, "app", CopilotNexusPaths.AppInstall);
                ctx.Status("Publishing Updater...");
                await PublishComponent(repoRoot, "updater", CopilotNexusPaths.AppInstall);
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
        var normalizedComponent = component.Trim().ToLowerInvariant();
        if (normalizedComponent is not ("nexus" or "app" or "cli" or "both"))
        {
            AnsiConsole.MarkupLine("[red]Invalid component.[/] Use [blue]nexus[/], [blue]app[/], [blue]cli[/], or [blue]both[/].");
            Environment.ExitCode = 1;
            return;
        }

        var components = normalizedComponent == "both" ? new[] { "nexus", "app" } : new[] { normalizedComponent };

        foreach (var comp in components)
        {
            if (comp == "cli")
            {
                await ApplyCliStagedUpdateAsync(reportIfMissing: true);
                continue;
            }

            var stagingPath = CopilotNexusPaths.GetStagingPath(comp);
            var installPath = CopilotNexusPaths.GetInstallPath(comp);

            if (!HasFiles(stagingPath))
            {
                AnsiConsole.MarkupLine($"[grey]No staged update for {comp}.[/]");
                if (comp == "nexus")
                    await ApplyCliStagedUpdateAsync();
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

                    // Copy staging to install with retry for lingering locks.
                    // Skip any stale CLI artifacts that may still exist in Nexus staging from older publish flows.
                    ctx.Status($"Copying {comp} files...");
                    await CopyDirectoryWithRetryAsync(
                        stagingPath,
                        installPath,
                        shouldCopyFile: comp == "nexus" ? file => !IsCliArtifact(file) : null);

                    // Clear staging
                    ctx.Status("Clearing staging...");
                    ClearDirectoryContents(stagingPath);

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

            if (comp == "nexus")
                await ApplyCliStagedUpdateAsync();
        }
    }

    // --- publish ---
    internal static async Task RunPublishAsync(string component)
    {
        var normalizedComponent = component.Trim().ToLowerInvariant();
        if (normalizedComponent is not ("nexus" or "app" or "cli" or "both"))
        {
            AnsiConsole.MarkupLine("[red]Invalid component.[/] Use [blue]nexus[/], [blue]app[/], [blue]cli[/], or [blue]both[/].");
            Environment.ExitCode = 1;
            return;
        }

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
        var components = normalizedComponent == "both" ? new[] { "nexus", "app" } : new[] { normalizedComponent };
        var cliPublishedToStaging = false;
        var publishVersion = CreatePublishVersion();

        AnsiConsole.MarkupLine($"[dim]Publish version: {Markup.Escape(publishVersion)}[/]");

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Publishing to staging...", async ctx =>
            {
                foreach (var comp in components)
                {
                    if (comp == "nexus")
                    {
                        var cliOutputPath = ResolveCliPublishOutputPath();
                        var stageCli = string.Equals(cliOutputPath, CopilotNexusPaths.CliStaging, StringComparison.OrdinalIgnoreCase);
                        ctx.Status(stageCli ? "Publishing CLI to staging..." : "Publishing CLI to install...");
                        await PublishComponent(repoRoot, "cli", cliOutputPath, publishVersion);
                        cliPublishedToStaging |= stageCli;

                        var stagingPath = CopilotNexusPaths.GetStagingPath(comp);
                        ctx.Status("Publishing Service to staging...");
                        await PublishComponent(repoRoot, "service", stagingPath, publishVersion);
                    }
                    else if (comp == "cli")
                    {
                        var cliOutputPath = ResolveCliPublishOutputPath();
                        var stageCli = string.Equals(cliOutputPath, CopilotNexusPaths.CliStaging, StringComparison.OrdinalIgnoreCase);
                        ctx.Status(stageCli ? "Publishing CLI to staging..." : "Publishing CLI to install...");
                        await PublishComponent(repoRoot, "cli", cliOutputPath, publishVersion);
                        cliPublishedToStaging |= stageCli;
                    }
                    else
                    {
                        var stagingPath = CopilotNexusPaths.GetStagingPath(comp);
                        if (comp == "app")
                        {
                            ctx.Status("Publishing App to staging...");
                            await PublishComponent(repoRoot, "app", stagingPath, publishVersion);
                            ctx.Status("Publishing Updater to staging...");
                            await PublishComponent(repoRoot, "updater", stagingPath, publishVersion);
                        }
                        else
                        {
                            ctx.Status($"Publishing {comp} to staging...");
                            await PublishComponent(repoRoot, comp, stagingPath, publishVersion);
                        }
                    }
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ Staged updates ready.[/]");
        if (cliPublishedToStaging)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]CLI binaries were staged to {Markup.Escape(CopilotNexusPaths.CliStaging)} to avoid self-overwrite locks.[/]");
            AnsiConsole.MarkupLine("[dim]Run [blue]nexus update --component cli[/] (or [blue]nexus update[/]) to apply the staged CLI update.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[dim]CLI binaries are published directly to {Markup.Escape(CopilotNexusPaths.CliInstall)}.[/]");
        }
        AnsiConsole.WriteLine();

        var panel = new Panel(
            "  [blue]nexus update[/]                      apply all staged updates\n" +
            "  [blue]nexus update --component nexus[/]    apply Nexus update only\n" +
            "  [blue]nexus update --component cli[/]      apply CLI update only\n" +
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

    private static async Task<StartValidationResult> ValidateStartedServiceAsync(string url, Process process)
    {
        var baseUrl = url.TrimEnd('/');
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        Exception? lastProbeError = null;

        while (DateTimeOffset.UtcNow < timeout)
        {
            if (process.HasExited)
            {
                var logError = TryGetLatestServiceErrorLine();
                var message = $"Service process exited early (PID {process.Id}, code {process.ExitCode}).";
                if (!string.IsNullOrWhiteSpace(logError))
                    message += $" Latest error: {logError}";
                return new StartValidationResult(false, message);
            }

            try
            {
                var healthResponse = await http.GetAsync($"{baseUrl}/health");
                if (healthResponse.IsSuccessStatusCode)
                {
                    var modelResponse = await http.GetAsync($"{baseUrl}/api/models");
                    if (!modelResponse.IsSuccessStatusCode)
                    {
                        var modelBody = await modelResponse.Content.ReadAsStringAsync();
                        return new StartValidationResult(
                            false,
                            $"Model catalog probe failed with {(int)modelResponse.StatusCode} {modelResponse.StatusCode}. {Tail(modelBody, 600)}");
                    }

                    var sessionProbe = await ProbeSessionCreateDeleteAsync(http, baseUrl);
                    if (sessionProbe != null)
                        return new StartValidationResult(false, sessionProbe);

                    return new StartValidationResult(true, "ready");
                }
            }
            catch (HttpRequestException ex)
            {
                lastProbeError = ex;
            }
            catch (TaskCanceledException ex)
            {
                lastProbeError = ex;
            }

            await Task.Delay(300);
        }

        var timeoutMessage = $"Service did not become healthy at {baseUrl} within 15 seconds.";
        if (lastProbeError != null)
            timeoutMessage += $" Last probe error: {lastProbeError.Message}";

        var latestError = TryGetLatestServiceErrorLine();
        if (!string.IsNullOrWhiteSpace(latestError))
            timeoutMessage += $" Latest error: {latestError}";

        return new StartValidationResult(false, timeoutMessage);
    }

    private static async Task<string?> ProbeSessionCreateDeleteAsync(HttpClient http, string baseUrl)
    {
        var createResponse = await http.PostAsJsonAsync($"{baseUrl}/api/sessions", new
        {
            Name = "Nexus startup validation",
            IsAutopilot = true,
            Model = "pi-auto",
        });

        if (!createResponse.IsSuccessStatusCode)
        {
            var createBody = await createResponse.Content.ReadAsStringAsync();
            return $"Session creation probe failed with {(int)createResponse.StatusCode} {createResponse.StatusCode}. {Tail(createBody, 600)}";
        }

        var createContent = await createResponse.Content.ReadAsStringAsync();
        string? sessionId = null;
        try
        {
            using var json = JsonDocument.Parse(createContent);
            if (json.RootElement.TryGetProperty("id", out var idValue))
                sessionId = idValue.GetString();
        }
        catch (JsonException)
        {
            // Keep startup validation resilient to non-JSON error payloads.
        }

        if (string.IsNullOrWhiteSpace(sessionId))
            return "Session creation probe succeeded but returned no session id.";

        var deleteResponse = await http.DeleteAsync($"{baseUrl}/api/sessions/{sessionId}");
        if (!deleteResponse.IsSuccessStatusCode && deleteResponse.StatusCode != HttpStatusCode.NotFound)
        {
            var deleteBody = await deleteResponse.Content.ReadAsStringAsync();
            return $"Session cleanup probe failed with {(int)deleteResponse.StatusCode} {deleteResponse.StatusCode}. {Tail(deleteBody, 600)}";
        }

        return null;
    }

    private static void TryStopProcess(Process process)
    {
        if (process.HasExited)
            return;

        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
    }

    private static string? TryGetLatestServiceErrorLine()
    {
        try
        {
            if (!Directory.Exists(CopilotNexusPaths.Logs))
                return null;

            var latestLog = Directory
                .EnumerateFiles(CopilotNexusPaths.Logs, "nexus-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (latestLog == null)
                return null;

            return File
                .ReadLines(latestLog)
                .Reverse()
                .FirstOrDefault(line => line.Contains("[ERR]", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
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

    private static async Task PublishComponent(string repoRoot, string component, string outputPath, string? version = null)
    {
        var projectPath = component switch
        {
            "cli" => Path.Combine(repoRoot, "src", "CopilotNexus.Cli", "CopilotNexus.Cli.csproj"),
            "service" => Path.Combine(repoRoot, "src", "CopilotNexus.Service", "CopilotNexus.Service.csproj"),
            "app" => Path.Combine(repoRoot, "src", "CopilotNexus.App", "CopilotNexus.App.csproj"),
            "updater" => Path.Combine(repoRoot, "src", "CopilotNexus.Updater", "CopilotNexus.Updater.csproj"),
            _ => throw new ArgumentException($"Unknown component: {component}"),
        };

        AnsiConsole.MarkupLine($"  [blue]{component}[/] → [dim]{Markup.Escape(outputPath)}[/]");
        var versionArgs = string.IsNullOrWhiteSpace(version)
            ? string.Empty
            : $" -p:Version={version} -p:InformationalVersion={version}";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectPath}\" -c Release -o \"{outputPath}\" --self-contained false --nologo -v q{versionArgs}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        WriteCliLog($"publish start [{component}] (version={version ?? "default"}): {psi.FileName} {psi.Arguments}");
        var result = await RunProcessWithCapturedOutputAsync(psi);
        WriteCliLog(
            $"publish complete [{component}]: exit={result.ExitCode}, elapsed={result.Elapsed.TotalSeconds:F1}s, " +
            $"stdoutChars={result.StandardOutput.Length}, stderrChars={result.StandardError.Length}");

        if (result.ExitCode != 0)
        {
            WriteCliLog($"publish failed [{component}] stdout tail:\n{Tail(result.StandardOutput)}");
            WriteCliLog($"publish failed [{component}] stderr tail:\n{Tail(result.StandardError)}");
            AnsiConsole.MarkupLine($"  [red]✗ Failed to publish {component}[/]");
            var errorOutput = !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError
                : result.StandardOutput;
            if (!string.IsNullOrWhiteSpace(errorOutput))
            {
                AnsiConsole.MarkupLine(Markup.Escape(Tail(errorOutput)));
            }
            Environment.ExitCode = 1;
        }
        else
        {
            AnsiConsole.MarkupLine($"  [green]✓[/] {component} published [dim]({result.Elapsed.TotalSeconds:F1}s)[/]");
        }
    }

    private static async Task<ProcessExecutionResult> RunProcessWithCapturedOutputAsync(ProcessStartInfo psi)
    {
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {psi.FileName} {psi.Arguments}");

        var sw = Stopwatch.StartNew();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await Task.WhenAll(proc.WaitForExitAsync(), stdoutTask, stderrTask);
        sw.Stop();

        return new ProcessExecutionResult(
            proc.ExitCode,
            stdoutTask.Result,
            stderrTask.Result,
            sw.Elapsed);
    }

    private static string Tail(string text, int maxLength = 4000)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text[^maxLength..];
    }

    private static async Task ApplyCliStagedUpdateAsync(bool reportIfMissing = false)
    {
        var stagingPath = CopilotNexusPaths.CliStaging;
        if (!HasFiles(stagingPath))
        {
            if (reportIfMissing)
                AnsiConsole.MarkupLine("[grey]No staged update for cli.[/]");
            return;
        }

        if (IsRunningFromCliInstall())
        {
            ScheduleCliSelfUpdateWorker(stagingPath, CopilotNexusPaths.CliInstall, Environment.ProcessId);
            AnsiConsole.MarkupLine("[yellow]CLI update scheduled. It will be applied after this command exits.[/]");
            return;
        }

        await CopyDirectoryWithRetryAsync(stagingPath, CopilotNexusPaths.CliInstall);
        ClearDirectoryContents(stagingPath);
        WriteCliLog("cli update applied from staging");
        AnsiConsole.MarkupLine("[green]✓[/] cli updated successfully.");
    }

    private static string ResolveCliPublishOutputPath()
    {
        var outputPath = IsRunningFromCliInstall()
            ? CopilotNexusPaths.CliStaging
            : CopilotNexusPaths.CliInstall;

        WriteCliLog($"cli publish target resolved to {outputPath}");
        return outputPath;
    }

    private static bool IsRunningFromCliInstall()
    {
        var cliInstallDir = Path.GetFullPath(CopilotNexusPaths.CliInstall)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var appBaseDir = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(cliInstallDir, appBaseDir, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(Environment.ProcessPath))
            return false;

        var currentProcessPath = Path.GetFullPath(Environment.ProcessPath);
        var installedCliPath = Path.GetFullPath(CopilotNexusPaths.CliExe);
        return string.Equals(currentProcessPath, installedCliPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void ScheduleCliSelfUpdateWorker(string stagingPath, string installPath, int parentPid)
    {
        var logPath = Path.Combine(CopilotNexusPaths.Logs, $"cli-{DateTime.UtcNow:yyyyMMdd}.log");
        var script = BuildCliSelfUpdateScript(parentPid, stagingPath, installPath, logPath);
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var worker = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to launch CLI self-update worker.");

        WriteCliLog($"scheduled cli self-update worker pid={worker.Id} for parentPid={parentPid}");
    }

    private static string BuildCliSelfUpdateScript(int parentPid, string stagingPath, string installPath, string logPath)
    {
        var escapedStagingPath = EscapePowerShellSingleQuoted(stagingPath);
        var escapedInstallPath = EscapePowerShellSingleQuoted(installPath);
        var escapedLogPath = EscapePowerShellSingleQuoted(logPath);

        return $$"""
$ErrorActionPreference = 'Stop'
$parentPid = {{parentPid}}
$source = '{{escapedStagingPath}}'
$destination = '{{escapedInstallPath}}'
$logPath = '{{escapedLogPath}}'

function Write-Log([string]$message) {
    Add-Content -Path $logPath -Value ("{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff zzz'), $message)
}

Write-Log "cli-self-update worker started (parentPid=$parentPid)"

for ($i = 0; $i -lt 120; $i++) {
    if (-not (Get-Process -Id $parentPid -ErrorAction SilentlyContinue)) {
        break
    }
    Start-Sleep -Milliseconds 500
}

for ($attempt = 1; $attempt -le 20; $attempt++) {
    try {
        if (-not (Test-Path $source)) {
            Write-Log "cli-self-update source missing; nothing to apply"
            exit 0
        }

        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $copy = Start-Process -FilePath "robocopy.exe" -ArgumentList @($source, $destination, "/MIR", "/R:5", "/W:1", "/NFL", "/NDL", "/NJH", "/NJS", "/NP") -Wait -PassThru -NoNewWindow
        if ($copy.ExitCode -gt 7) {
            throw "robocopy exit code $($copy.ExitCode)"
        }

        Remove-Item -Path $source -Recurse -Force -ErrorAction SilentlyContinue
        Write-Log "cli-self-update applied successfully on attempt $attempt (robocopyExit=$($copy.ExitCode))"
        exit 0
    }
    catch {
        if ($attempt -eq 20) {
            Write-Log ("cli-self-update failed: " + $_.Exception.Message)
            exit 1
        }

        Start-Sleep -Seconds 1
    }
}
""";
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''");
    }

    private static bool HasFiles(string path)
    {
        return Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
    }

    private static string CreatePublishVersion()
    {
        return $"{PublishVersionBase}-{PublishVersionChannel}.{DateTime.UtcNow:yyyyMMddHHmmss}";
    }

    private static string? GetExecutableVersion(string exePath)
    {
        if (!File.Exists(exePath))
            return null;

        var version = FileVersionInfo.GetVersionInfo(exePath).ProductVersion;
        if (!string.IsNullOrWhiteSpace(version))
            return version;

        var assemblyVersion = FileVersionInfo.GetVersionInfo(exePath).FileVersion;
        return string.IsNullOrWhiteSpace(assemblyVersion) ? null : assemblyVersion;
    }

    private static void ClearDirectoryContents(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.Delete(file);

        foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            Directory.Delete(dir, true);
    }

    private static void WriteCliLog(string message)
    {
        var logPath = Path.Combine(CopilotNexusPaths.Logs, $"cli-{DateTime.UtcNow:yyyyMMdd}.log");
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}";
        File.AppendAllText(logPath, line + Environment.NewLine);
    }

    private static bool IsCliArtifact(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.Equals("CopilotNexus.Cli.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("CopilotNexus.Cli.", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string destination, Func<string, bool>? shouldCopyFile = null)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            if (shouldCopyFile != null && !shouldCopyFile(file))
                continue;

            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destDir, shouldCopyFile);
        }
    }

    private static async Task CopyDirectoryWithRetryAsync(
        string source,
        string destination,
        int maxRetries = 10,
        Func<string, bool>? shouldCopyFile = null)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                CopyDirectory(source, destination, shouldCopyFile);
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(1000);
            }
        }
        CopyDirectory(source, destination, shouldCopyFile);
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
