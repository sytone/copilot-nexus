using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using CopilotNexus.Core;
using CopilotNexus.Core.Versioning;
using Spectre.Console;

record HealthResponse(string? Status, int Sessions, int Models, string? Uptime);

/// <summary>
/// All CLI command implementations. CLI binaries are installed separately from the
/// Nexus service binaries to reduce self-update lock contention.
/// </summary>
internal static class CliCommands
{
    private const string InitialPublishBaseVersion = "0.1.0";
    private sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Elapsed);
    private sealed record StartValidationResult(bool IsReady, string Message);
    private sealed record ServiceLockInfo(int Pid, string? ExecutablePath, string? Version, string? Url, DateTimeOffset StartedAtUtc);
    private sealed record PublishVersionState(string BaseVersion, string? LastCommitSha);
    private sealed record PublishVersionPlan(
        SemanticVersion NextBaseVersion,
        string PublishVersion,
        bool IsReleaseMode,
        string? HeadCommitSha,
        VersionBump Bump,
        int CommitCount);
    private enum VersionBump
    {
        None = 0,
        Patch = 1,
        Minor = 2,
        Major = 3,
    }

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
        AnsiConsole.MarkupLine(
            $"  CLI: [dim]{Markup.Escape(GetComponentVersionDisplay(CopilotNexusPaths.CliExe, CopilotNexusPaths.CliInstall, "CopilotNexus.Cli.exe"))}[/]");
        AnsiConsole.MarkupLine(
            $"  Service: [dim]{Markup.Escape(GetComponentVersionDisplay(CopilotNexusPaths.ServiceExe, CopilotNexusPaths.NexusInstall, "CopilotNexus.Service.exe"))}[/]");
        AnsiConsole.MarkupLine(
            $"  App: [dim]{Markup.Escape(GetComponentVersionDisplay(CopilotNexusPaths.AppExe, CopilotNexusPaths.AppInstall, "CopilotNexus.App.exe"))}[/]");
    }

    // --- start ---
    internal static void RunStart(string url)
    {
        // Check if already running
        var lockInfo = ReadServiceLockInfo();
        if (lockInfo != null)
        {
            try
            {
                Process.GetProcessById(lockInfo.Pid);
                AnsiConsole.MarkupLine($"[yellow]Nexus is already running[/] (PID [bold]{lockInfo.Pid}[/]).");
                AnsiConsole.MarkupLine("Use [blue]nexus stop[/] first.");
                return;
            }
            catch (ArgumentException)
            {
                // Process not running — stale lock file, continue
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

        var resolvedVersion = TryExtractVersionFromExecutablePath(serviceExe);
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

        WriteServiceLockInfo(new ServiceLockInfo(
            proc.Id,
            serviceExe,
            resolvedVersion,
            url,
            DateTimeOffset.UtcNow));

        var validation = ValidateStartedServiceAsync(url, proc).GetAwaiter().GetResult();
        if (!validation.IsReady)
        {
            TryStopProcess(proc);
            TryDeleteLockFile();
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
        var lockInfo = ReadServiceLockInfo();
        if (lockInfo == null)
        {
            AnsiConsole.MarkupLine("[yellow]Nexus is not running[/] (no lock file found)");
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            var proc = Process.GetProcessById(lockInfo.Pid);
            AnsiConsole.MarkupLine($"Stopping Nexus (PID [bold]{lockInfo.Pid}[/])...");
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(10_000);
            AnsiConsole.MarkupLine("[green]✓[/] Nexus stopped.");
        }
        catch (ArgumentException)
        {
            AnsiConsole.MarkupLine($"[yellow]Nexus process (PID {lockInfo.Pid}) is not running.[/] Cleaning up lock file.");
        }

        TryDeleteLockFile();
    }

    // --- status ---
    internal static async Task RunStatusAsync(string url)
    {
        var baseUrl = url.TrimEnd('/');

        string processStatus;
        string pidDisplay;
        var lockInfo = ReadServiceLockInfo();
        if (lockInfo != null)
        {
            try
            {
                Process.GetProcessById(lockInfo.Pid);
                processStatus = "[green]Running[/]";
                pidDisplay = lockInfo.Pid.ToString();
            }
            catch (ArgumentException)
            {
                processStatus = "[yellow]Stale lock file[/]";
                pidDisplay = $"{lockInfo.Pid} (dead)";
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
        if (!string.IsNullOrWhiteSpace(lockInfo?.Version))
            table.AddRow("Service version", Markup.Escape(lockInfo!.Version!));
        if (!string.IsNullOrWhiteSpace(lockInfo?.ExecutablePath))
            table.AddRow("Service path", Markup.Escape(lockInfo!.ExecutablePath!));
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
            AnsiConsole.MarkupLine("Next: [blue]nexus publish[/] to publish a new versioned payload.");
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

        var versionPlan = CreatePublishVersionPlan(repoRoot);
        AnsiConsole.MarkupLine($"[dim]Install version: {Markup.Escape(versionPlan.PublishVersion)}[/]");

        var cliOutput = CopilotNexusPaths.GetVersionedInstallPath("cli", versionPlan.PublishVersion);
        var serviceOutput = CopilotNexusPaths.GetVersionedInstallPath("nexus", versionPlan.PublishVersion);
        var appOutput = CopilotNexusPaths.GetVersionedInstallPath("app", versionPlan.PublishVersion);

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Publishing components...", async ctx =>
            {
                ctx.Status("Installing component shims...");
                await PublishAndInstallShimsAsync(repoRoot, versionPlan.PublishVersion);

                ctx.Status("Publishing CLI payload...");
                await PublishComponent(repoRoot, "cli", cliOutput, versionPlan.PublishVersion);
                ctx.Status("Publishing Service payload...");
                await PublishComponent(repoRoot, "service", serviceOutput, versionPlan.PublishVersion);
                ctx.Status("Publishing Win App payload...");
                await PublishComponent(repoRoot, "app", appOutput, versionPlan.PublishVersion);
                ctx.Status("Publishing Updater payload...");
                await PublishComponent(repoRoot, "updater", appOutput, versionPlan.PublishVersion);
            });

        if (Environment.ExitCode == 0)
            SavePublishVersionState(versionPlan);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ Installation complete.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Set up the [blue]nexus[/] alias for easy access:");
        AnsiConsole.MarkupLine($"  [dim]Set-Alias nexus \"{Markup.Escape(CopilotNexusPaths.CliExe)}\"[/]");
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

        var shimsInstalled = File.Exists(CopilotNexusPaths.CliExe)
            && File.Exists(CopilotNexusPaths.ServiceExe)
            && File.Exists(CopilotNexusPaths.AppExe);
        if (!shimsInstalled)
        {
            AnsiConsole.MarkupLine("[red]Component shims are not installed.[/]");
            AnsiConsole.MarkupLine($"  Expected shims under: [dim]{Markup.Escape(CopilotNexusPaths.AppRoot)}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Run [blue]nexus install[/] first.");
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
        var versionPlan = CreatePublishVersionPlan(repoRoot);
        var releaseMode = versionPlan.IsReleaseMode ? "release" : "dev";
        AnsiConsole.MarkupLine($"[dim]Publish version: {Markup.Escape(versionPlan.PublishVersion)} ({releaseMode})[/]");
        AnsiConsole.MarkupLine($"[dim]Version bump: {versionPlan.Bump} from {versionPlan.CommitCount} commit(s)[/]");

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Publishing versioned payloads...", async ctx =>
            {
                ctx.Status("Refreshing shims...");
                await PublishAndInstallShimsAsync(repoRoot, versionPlan.PublishVersion);

                foreach (var comp in components)
                {
                    if (comp == "nexus")
                    {
                        var cliOutput = CopilotNexusPaths.GetVersionedInstallPath("cli", versionPlan.PublishVersion);
                        var serviceOutput = CopilotNexusPaths.GetVersionedInstallPath("nexus", versionPlan.PublishVersion);
                        ctx.Status("Publishing CLI payload...");
                        await PublishComponent(repoRoot, "cli", cliOutput, versionPlan.PublishVersion);
                        ctx.Status("Publishing Service payload...");
                        await PublishComponent(repoRoot, "service", serviceOutput, versionPlan.PublishVersion);
                    }
                    else if (comp == "cli")
                    {
                        var cliOutput = CopilotNexusPaths.GetVersionedInstallPath("cli", versionPlan.PublishVersion);
                        ctx.Status("Publishing CLI payload...");
                        await PublishComponent(repoRoot, "cli", cliOutput, versionPlan.PublishVersion);
                    }
                    else
                    {
                        var appOutput = CopilotNexusPaths.GetVersionedInstallPath("app", versionPlan.PublishVersion);
                        if (comp == "app")
                        {
                            ctx.Status("Publishing Win App payload...");
                            await PublishComponent(repoRoot, "app", appOutput, versionPlan.PublishVersion);
                            ctx.Status("Publishing Updater payload...");
                            await PublishComponent(repoRoot, "updater", appOutput, versionPlan.PublishVersion);
                        }
                        else
                        {
                            var output = CopilotNexusPaths.GetVersionedInstallPath(comp, versionPlan.PublishVersion);
                            ctx.Status($"Publishing {comp} payload...");
                            await PublishComponent(repoRoot, comp, output, versionPlan.PublishVersion);
                        }
                    }
                }
            });

        if (Environment.ExitCode == 0)
            SavePublishVersionState(versionPlan);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ Versioned publish complete.[/]");
        AnsiConsole.MarkupLine("[dim]No update-copy step is required. Launch via shims to pick up newest version.[/]");
        AnsiConsole.WriteLine();

        var panel = new Panel(
            "  [blue]nexus stop && nexus start[/]         restart service on newest published version\n" +
            "  [blue]nexus winapp start[/]                launch desktop app shim (latest by default)\n" +
            "  [blue]nexus version[/]                     inspect installed shim and payload versions\n\n" +
            "  [dim]Use shim flags directly for rollback/retention: --previous, --cleanup <N>.[/]")
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
        if (File.Exists(CopilotNexusPaths.ServiceExe))
            return CopilotNexusPaths.ServiceExe;

        try
        {
            return VersionedExecutableResolver
                .ResolveExecutable(CopilotNexusPaths.NexusInstall, "CopilotNexus.Service.exe")
                .ExecutablePath;
        }
        catch (InvalidOperationException)
        {
            // Fall back to source build paths for developer workflows.
        }

        string[] searchPaths =
        [
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
        if (File.Exists(CopilotNexusPaths.AppExe))
            return CopilotNexusPaths.AppExe;

        try
        {
            return VersionedExecutableResolver
                .ResolveExecutable(CopilotNexusPaths.AppInstall, "CopilotNexus.App.exe")
                .ExecutablePath;
        }
        catch (InvalidOperationException)
        {
            // Fall back to source build paths for developer workflows.
        }

        string[] searchPaths =
        [
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

    private static ServiceLockInfo? ReadServiceLockInfo()
    {
        if (!File.Exists(CopilotNexusPaths.NexusLockFile))
            return null;

        try
        {
            var content = File.ReadAllText(CopilotNexusPaths.NexusLockFile).Trim();
            if (int.TryParse(content, out var legacyPid))
            {
                return new ServiceLockInfo(
                    legacyPid,
                    null,
                    null,
                    null,
                    File.GetLastWriteTimeUtc(CopilotNexusPaths.NexusLockFile));
            }

            var parsed = JsonSerializer.Deserialize<ServiceLockInfo>(content);
            return parsed?.Pid > 0 ? parsed : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteServiceLockInfo(ServiceLockInfo lockInfo)
    {
        var json = JsonSerializer.Serialize(lockInfo, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CopilotNexusPaths.NexusLockFile, json);
    }

    private static void TryDeleteLockFile()
    {
        try
        {
            if (File.Exists(CopilotNexusPaths.NexusLockFile))
                File.Delete(CopilotNexusPaths.NexusLockFile);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    private static string? TryExtractVersionFromExecutablePath(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(directory) &&
            SemanticVersion.TryParse(Path.GetFileName(directory), out var folderVersion) &&
            folderVersion != null)
        {
            return folderVersion.ToString();
        }

        if (string.Equals(
                Path.GetFullPath(executablePath),
                Path.GetFullPath(CopilotNexusPaths.ServiceExe),
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return VersionedExecutableResolver
                    .ResolveExecutable(CopilotNexusPaths.NexusInstall, "CopilotNexus.Service.exe")
                    .Version
                    .ToString();
            }
            catch (InvalidOperationException)
            {
                // Fall through to file version metadata.
            }
        }

        return GetExecutableVersion(executablePath);
    }

    private static async Task PublishAndInstallShimsAsync(string repoRoot, string version)
    {
        await PublishShimExecutableAsync(repoRoot, CopilotNexusPaths.CliInstall, "CopilotNexus.Cli", version, "CLI shim");
        await PublishShimExecutableAsync(repoRoot, CopilotNexusPaths.NexusInstall, "CopilotNexus.Service", version, "Service shim");
        await PublishShimExecutableAsync(repoRoot, CopilotNexusPaths.AppInstall, "CopilotNexus.App", version, "App shim");
    }

    private static async Task PublishShimExecutableAsync(
        string repoRoot,
        string outputPath,
        string assemblyName,
        string version,
        string label)
    {
        var projectPath = Path.Combine(repoRoot, "src", "CopilotNexus.Shim", "CopilotNexus.Shim.csproj");
        if (!File.Exists(projectPath))
            throw new FileNotFoundException("Shim project was not found.", projectPath);

        Directory.CreateDirectory(outputPath);
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments =
                $"publish \"{projectPath}\" -c Release -o \"{outputPath}\" --self-contained false --nologo -v q " +
                $"-p:AssemblyName={assemblyName} -p:Version={version} -p:InformationalVersion={version}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        WriteCliLog($"publish start [{label}] (version={version}): {psi.FileName} {psi.Arguments}");
        var result = await RunProcessWithCapturedOutputAsync(psi);
        if (result.ExitCode != 0)
        {
            WriteCliLog($"publish failed [{label}] stdout tail:\n{Tail(result.StandardOutput)}");
            WriteCliLog($"publish failed [{label}] stderr tail:\n{Tail(result.StandardError)}");
            throw new InvalidOperationException($"Failed to publish {label}. {Tail(result.StandardError)}");
        }
    }

    private static PublishVersionPlan CreatePublishVersionPlan(string repoRoot)
    {
        var headCommitSha = GetHeadCommitSha(repoRoot);
        if (TryGetReleaseVersion(out var releaseVersion))
        {
            return new PublishVersionPlan(
                releaseVersion!,
                releaseVersion!.ToString(),
                true,
                headCommitSha,
                VersionBump.None,
                0);
        }

        var previousState = TryReadPublishVersionState();
        var currentBase = TryParseBaseVersion(previousState?.BaseVersion) ?? SemanticVersion.Parse(InitialPublishBaseVersion);
        var commits = GetCommitMessagesSince(repoRoot, previousState?.LastCommitSha);
        var bump = InferVersionBump(commits);
        var nextBase = ApplyVersionBump(currentBase, bump);
        var publishVersion = $"{nextBase}-dev.{DateTime.UtcNow:yyyyMMddHHmmss}";

        return new PublishVersionPlan(nextBase, publishVersion, false, headCommitSha, bump, commits.Count);
    }

    private static PublishVersionState? TryReadPublishVersionState()
    {
        if (!File.Exists(CopilotNexusPaths.PublishVersionStateFile))
            return null;

        try
        {
            var json = File.ReadAllText(CopilotNexusPaths.PublishVersionStateFile);
            return JsonSerializer.Deserialize<PublishVersionState>(json);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void SavePublishVersionState(PublishVersionPlan plan)
    {
        var state = new PublishVersionState(
            plan.NextBaseVersion.ToString(),
            plan.HeadCommitSha);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(CopilotNexusPaths.PublishVersionStateFile)!);
        File.WriteAllText(CopilotNexusPaths.PublishVersionStateFile, json);
    }

    private static SemanticVersion? TryParseBaseVersion(string? value)
    {
        return SemanticVersion.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? GetHeadCommitSha(string repoRoot)
    {
        return TryRunGit(repoRoot, "rev-parse HEAD");
    }

    private static IReadOnlyList<string> GetCommitMessagesSince(string repoRoot, string? fromCommitExclusive)
    {
        var logArgs = string.IsNullOrWhiteSpace(fromCommitExclusive)
            ? "log -n 1 --pretty=format:%s%n%b%x1e HEAD"
            : $"log --pretty=format:%s%n%b%x1e {fromCommitExclusive}..HEAD";
        var output = TryRunGit(repoRoot, logArgs);
        if (string.IsNullOrWhiteSpace(output))
            return [];

        return output
            .Split('\x1e', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim())
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .ToList();
    }

    private static VersionBump InferVersionBump(IReadOnlyList<string> commitMessages)
    {
        if (commitMessages.Count == 0)
            return VersionBump.None;

        var highest = VersionBump.Patch;
        foreach (var message in commitMessages)
        {
            var parsed = ParseCommitBump(message);
            if (parsed > highest)
                highest = parsed;
            if (highest == VersionBump.Major)
                break;
        }

        return highest;
    }

    private static VersionBump ParseCommitBump(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return VersionBump.None;

        var lines = message
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        var header = lines.FirstOrDefault() ?? string.Empty;
        if (message.Contains("BREAKING CHANGE", StringComparison.OrdinalIgnoreCase))
            return VersionBump.Major;

        if (header.Contains("!:", StringComparison.Ordinal))
            return VersionBump.Major;

        if (header.StartsWith("feat(", StringComparison.OrdinalIgnoreCase) ||
            header.StartsWith("feat:", StringComparison.OrdinalIgnoreCase))
            return VersionBump.Minor;

        return VersionBump.Patch;
    }

    private static SemanticVersion ApplyVersionBump(SemanticVersion baseVersion, VersionBump bump)
    {
        return bump switch
        {
            VersionBump.Major => baseVersion.NextMajor(),
            VersionBump.Minor => baseVersion.NextMinor(),
            VersionBump.Patch => baseVersion.NextPatch(),
            _ => baseVersion,
        };
    }

    private static bool TryGetReleaseVersion(out SemanticVersion? releaseVersion)
    {
        if (TryParseReleaseVersion(Environment.GetEnvironmentVariable("NEXUS_RELEASE_VERSION"), out releaseVersion))
            return true;

        var refType = Environment.GetEnvironmentVariable("GITHUB_REF_TYPE");
        var refName = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
        if (string.Equals(refType, "tag", StringComparison.OrdinalIgnoreCase) &&
            TryParseReleaseVersion(refName, out releaseVersion))
        {
            return true;
        }

        releaseVersion = null;
        return false;
    }

    private static bool TryParseReleaseVersion(string? value, out SemanticVersion? releaseVersion)
    {
        releaseVersion = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.StartsWith("refs/tags/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["refs/tags/".Length..];
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];

        if (!SemanticVersion.TryParse(normalized, out var parsed) || parsed == null)
            return false;

        releaseVersion = parsed;
        return true;
    }

    private static string? TryRunGit(string repoRoot, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{repoRoot}\" --no-pager {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return null;

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                WriteCliLog($"git command failed: git {psi.Arguments}\n{Tail(stderr)}");
                return null;
            }

            return stdout.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            WriteCliLog($"git command failed: git {psi.Arguments}. {ex.Message}");
            return null;
        }
    }

    private static string GetComponentVersionDisplay(string shimExePath, string componentRoot, string executableName)
    {
        var shimVersion = GetExecutableVersion(shimExePath);
        string? payloadVersion = null;
        try
        {
            payloadVersion = VersionedExecutableResolver
                .ResolveExecutable(componentRoot, executableName)
                .Version
                .ToString();
        }
        catch (InvalidOperationException)
        {
            // No versioned payloads available yet.
        }

        if (shimVersion == null && payloadVersion == null)
            return "not installed";
        if (shimVersion == null)
            return $"payload {payloadVersion}";
        if (payloadVersion == null)
            return $"shim {shimVersion}";

        return $"shim {shimVersion}, payload {payloadVersion}";
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

    private static void WriteCliLog(string message)
    {
        var logPath = Path.Combine(CopilotNexusPaths.Logs, $"cli-{DateTime.UtcNow:yyyyMMdd}.log");
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}";
        File.AppendAllText(logPath, line + Environment.NewLine);
    }
}
