using CopilotNexus.Core;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Service;
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
    var builder = NexusHostBuilder.CreateBuilder(args);
    var app = NexusHostBuilder.ConfigureApp(builder.Build());

    var sessionManager = app.Services.GetRequiredService<ISessionManager>();
    await sessionManager.InitializeAsync();

    Log.Information("Nexus service starting (PID {Pid})", Environment.ProcessId);
    await app.RunAsync();
}
finally
{
    if (!isTestMode)
    {
        try { File.Delete(CopilotNexusPaths.NexusLockFile); } catch { }
    }
}

/// <summary>Entry point anchor for WebApplicationFactory in integration tests.</summary>
public partial class Program { }
