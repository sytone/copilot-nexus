using CopilotNexus.Core;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using CopilotNexus.Core.Services;
using CopilotNexus.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

// CopilotNexus.Service — pure ASP.NET Core web host.
// Started by the CLI ('nexus start') or directly for development.
// All management commands live in CopilotNexus.Cli.

CopilotNexusPaths.EnsureDirectories();

// Write PID lock file so the CLI can find and stop this process.
// Skip when running inside WebApplicationFactory (tests set NEXUS_TEST_MODE).
var isTestMode = Environment.GetEnvironmentVariable("NEXUS_TEST_MODE") == "1";
if (!isTestMode)
{
    File.WriteAllText(CopilotNexusPaths.NexusLockFile, Environment.ProcessId.ToString());
}

try
{
    var startupArgs = ServiceStartupArgumentParser.Parse(args);
    if (!string.IsNullOrWhiteSpace(startupArgs.Error))
    {
        Console.Error.WriteLine(startupArgs.Error);
        Environment.ExitCode = 1;
        return;
    }

    var runtimeConfigService = new JsonRuntimeAgentConfigService(
        NullLogger<JsonRuntimeAgentConfigService>.Instance,
        GetRuntimeConfigPath(isTestMode));
    var configuredAgent = await runtimeConfigService.GetAsync();
    var selectedAgent = startupArgs.AgentOverride ?? configuredAgent;

    var builder = NexusHostBuilder.CreateBuilder(selectedAgent, startupArgs.ForwardedArgs.ToArray());
    var app = NexusHostBuilder.ConfigureApp(builder.Build());

    var sessionManager = app.Services.GetRequiredService<ISessionManager>();
    await sessionManager.InitializeAsync();

    if (startupArgs.AgentOverride.HasValue)
    {
        await runtimeConfigService.SetAsync(selectedAgent);
    }

    Log.Information(
        "Nexus service starting (PID {Pid}, runtime={Runtime})",
        Environment.ProcessId,
        selectedAgent.ToConfigValue());
    await app.RunAsync();
}
finally
{
    if (!isTestMode)
    {
        try { File.Delete(CopilotNexusPaths.NexusLockFile); } catch { }
    }
}

static string GetRuntimeConfigPath(bool isTestMode)
{
    if (!isTestMode)
        return CopilotNexusPaths.RuntimeAgentConfigFile;

    var testStateRoot = Path.Combine(Path.GetTempPath(), "CopilotNexus", "test-state");
    Directory.CreateDirectory(testStateRoot);
    return Path.Combine(testStateRoot, $"runtime-config-{Environment.ProcessId}.json");
}

/// <summary>Entry point anchor for WebApplicationFactory in integration tests.</summary>
public partial class Program { }
