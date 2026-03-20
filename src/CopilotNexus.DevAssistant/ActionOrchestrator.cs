using System.Diagnostics;
using System.Text.Json;
using CopilotNexus.Core;

namespace CopilotNexus.DevAssistant;

/// <summary>
/// Orchestrates build, publish, and restart actions by executing dotnet CLI commands.
/// </summary>
internal sealed class ActionOrchestrator
{
    private readonly string _repoRoot;
    private readonly ILogger<ActionOrchestrator> _logger;

    private sealed record ServiceLockInfo(
        int Pid,
        string? ExecutablePath,
        string? Version,
        string? Url,
        DateTimeOffset StartedAtUtc,
        string? Agent = null);

    public ActionOrchestrator(string repoRoot, ILogger<ActionOrchestrator> logger)
    {
        _repoRoot = repoRoot;
        _logger = logger;
    }

    public async Task<ActionResult> RebuildAsync()
    {
        var slnPath = Path.Combine(_repoRoot, "CopilotNexus.slnx");
        _logger.LogInformation("Starting rebuild: {Solution}", slnPath);

        var result = await RunDotnetAsync($"build \"{slnPath}\" -c Release --nologo -v q");
        return new ActionResult("rebuild", result.ExitCode == 0, result.Output, result.Elapsed);
    }

    public async Task<ActionResult> PublishAsync()
    {
        _logger.LogInformation("Starting publish for all components");

        var components = new[]
        {
            ("Service", "src/CopilotNexus.Service/CopilotNexus.Service.csproj"),
            ("App", "src/CopilotNexus.App/CopilotNexus.App.csproj"),
            ("Cli", "src/CopilotNexus.Cli/CopilotNexus.Cli.csproj"),
        };

        var allOutput = new List<string>();
        var allSuccess = true;

        foreach (var (name, projectPath) in components)
        {
            var fullPath = Path.Combine(_repoRoot, projectPath);
            var result = await RunDotnetAsync(
                $"publish \"{fullPath}\" -c Release --self-contained false --nologo -v q");

            allOutput.Add($"--- {name} (exit={result.ExitCode}, {result.Elapsed.TotalSeconds:F1}s) ---");
            allOutput.Add(result.Output);

            if (result.ExitCode != 0)
            {
                allSuccess = false;
                _logger.LogError("Publish failed for {Component}", name);
                break;
            }

            _logger.LogInformation("Published {Component}", name);
        }

        return new ActionResult("publish", allSuccess, string.Join("\n", allOutput), TimeSpan.Zero);
    }

    public Task<ActionResult> RestartAsync()
    {
        _logger.LogInformation("Starting restart sequence");

        // Stop
        var stopResult = StopService();

        // Start
        var startResult = StartService();

        var success = startResult.Success;
        var output = $"Stop: {(stopResult.Success ? "OK" : "FAILED")}\nStart: {(startResult.Success ? "OK" : "FAILED")}\n{startResult.Output}";

        return Task.FromResult(new ActionResult("restart", success, output, TimeSpan.Zero));
    }

    public async Task<ActionResult> RepublishAsync()
    {
        _logger.LogInformation("Starting republish sequence (rebuild → publish → restart)");

        var steps = new List<ActionResult>();

        var rebuild = await RebuildAsync();
        steps.Add(rebuild);
        if (!rebuild.Success)
            return AggregateResult("republish", steps, failedAt: "rebuild");

        var publish = await PublishAsync();
        steps.Add(publish);
        if (!publish.Success)
            return AggregateResult("republish", steps, failedAt: "publish");

        var restart = await RestartAsync();
        steps.Add(restart);

        return AggregateResult("republish", steps, failedAt: restart.Success ? null : "restart");
    }

    private ActionResult StopService()
    {
        var lockFile = CopilotNexusPaths.NexusLockFile;
        if (!File.Exists(lockFile))
            return new ActionResult("stop", true, "No running service found (no lock file)", TimeSpan.Zero);

        try
        {
            var json = File.ReadAllText(lockFile);
            var lockInfo = JsonSerializer.Deserialize<ServiceLockInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (lockInfo == null)
                return new ActionResult("stop", true, "Empty lock file", TimeSpan.Zero);

            try
            {
                var proc = Process.GetProcessById(lockInfo.Pid);
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(10_000);
                _logger.LogInformation("Stopped Nexus service (PID {Pid})", lockInfo.Pid);
            }
            catch (ArgumentException)
            {
                _logger.LogInformation("Nexus process (PID {Pid}) was not running", lockInfo.Pid);
            }

            File.Delete(lockFile);
            return new ActionResult("stop", true, $"Stopped PID {lockInfo.Pid}", TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop service");
            return new ActionResult("stop", false, ex.Message, TimeSpan.Zero);
        }
    }

    private ActionResult StartService()
    {
        var serviceExe = CopilotNexusPaths.ServiceExe;

        // Find versioned executable if shim doesn't exist
        if (!File.Exists(serviceExe))
        {
            _logger.LogError("Service executable not found: {Path}", serviceExe);
            return new ActionResult("start", false, $"Service executable not found: {serviceExe}", TimeSpan.Zero);
        }

        var url = "http://localhost:5280";
        var psi = new ProcessStartInfo
        {
            FileName = serviceExe,
            Arguments = $"--urls {url}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            var proc = Process.Start(psi);
            if (proc == null)
                return new ActionResult("start", false, "Failed to start process", TimeSpan.Zero);

            // Write lock file
            var lockInfo = new ServiceLockInfo(proc.Id, serviceExe, null, url, DateTimeOffset.UtcNow);
            var lockJson = JsonSerializer.Serialize(lockInfo, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CopilotNexusPaths.NexusLockFile, lockJson);

            _logger.LogInformation("Started Nexus service (PID {Pid}) at {Url}", proc.Id, url);
            return new ActionResult("start", true, $"Started PID {proc.Id} at {url}", TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start service");
            return new ActionResult("start", false, ex.Message, TimeSpan.Zero);
        }
    }

    private async Task<ProcessResult> RunDotnetAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _repoRoot,
        };

        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process");

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        sw.Stop();

        var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";
        // Return last 50 lines max
        var lines = combined.Split('\n');
        var tail = lines.Length > 50 ? string.Join("\n", lines[^50..]) : combined;

        return new ProcessResult(proc.ExitCode, tail.Trim(), sw.Elapsed);
    }

    private static ActionResult AggregateResult(string action, List<ActionResult> steps, string? failedAt)
    {
        var output = string.Join("\n\n", steps.Select(s =>
            $"[{s.Action}] {(s.Success ? "OK" : "FAILED")}\n{s.Output}"));

        if (failedAt != null)
            output = $"Failed at step: {failedAt}\n\n{output}";

        return new ActionResult(action, failedAt == null, output, TimeSpan.Zero);
    }

    private sealed record ProcessResult(int ExitCode, string Output, TimeSpan Elapsed);
}

internal sealed record ActionResult(string Action, bool Success, string Output, TimeSpan Elapsed);
