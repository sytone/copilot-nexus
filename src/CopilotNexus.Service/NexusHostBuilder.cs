namespace CopilotNexus.Service;

using CopilotNexus.Core;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using CopilotNexus.Core.Services;
using CopilotNexus.Service.Hubs;
using Serilog;

/// <summary>
/// Builds and configures the Nexus web application.
/// Shared by the CLI entry point and integration test factory.
/// </summary>
public static class NexusHostBuilder
{
    /// <summary>
    /// Creates a fully configured WebApplicationBuilder with all Nexus services registered.
    /// </summary>
    public static WebApplicationBuilder CreateBuilder(string[]? args = null)
        => CreateBuilder(RuntimeAgentType.Pi, args);

    /// <summary>
    /// Creates a fully configured WebApplicationBuilder with all Nexus services registered.
    /// </summary>
    public static WebApplicationBuilder CreateBuilder(RuntimeAgentType runtimeAgent, string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);

        // Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(CopilotNexusPaths.Logs, "nexus-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        builder.Host.UseSerilog();

        // Services
        builder.Services.AddSignalR();
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSingleton<IRuntimeAgentConfigService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<JsonRuntimeAgentConfigService>>();
            return new JsonRuntimeAgentConfigService(logger, GetRuntimeConfigPath());
        });

        builder.Services.AddSingleton<IAgentClientService>(sp =>
        {
            return runtimeAgent switch
            {
                RuntimeAgentType.Pi => new PiRpcClientService(
                    sp.GetRequiredService<ILogger<PiRpcClientService>>()),
                RuntimeAgentType.CopilotSdk => new CopilotClientService(
                    sp.GetRequiredService<ILogger<CopilotClientService>>()),
                _ => throw new InvalidOperationException($"Unsupported runtime agent '{runtimeAgent}'."),
            };
        });

        builder.Services.AddSingleton<ISessionManager>(sp =>
        {
            var client = sp.GetRequiredService<IAgentClientService>();
            var logger = sp.GetRequiredService<ILogger<SessionManager>>();
            return new SessionManager(client, logger);
        });

        builder.Services.AddSingleton<IStatePersistenceService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<JsonStatePersistenceService>>();
            return new JsonStatePersistenceService(logger, GetServiceStatePath());
        });

        builder.Services.AddSingleton<ISessionProfileService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<JsonSessionProfileService>>();
            return new JsonSessionProfileService(logger, GetServiceProfilesPath());
        });

        // CORS for local development
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials()
                      .SetIsOriginAllowed(_ => true));
        });

        return builder;
    }

    private static string GetServiceStatePath()
    {
        var isTestMode = Environment.GetEnvironmentVariable("NEXUS_TEST_MODE") == "1";
        if (isTestMode)
        {
            var testStateRoot = Path.Combine(Path.GetTempPath(), "CopilotNexus", "test-state");
            Directory.CreateDirectory(testStateRoot);
            return Path.Combine(testStateRoot, $"session-state-{Environment.ProcessId}.json");
        }

        return CopilotNexusPaths.NexusAppStateFile;
    }

    private static string GetServiceProfilesPath()
    {
        var isTestMode = Environment.GetEnvironmentVariable("NEXUS_TEST_MODE") == "1";
        if (isTestMode)
        {
            var testStateRoot = Path.Combine(Path.GetTempPath(), "CopilotNexus", "test-state");
            Directory.CreateDirectory(testStateRoot);
            return Path.Combine(testStateRoot, $"session-profiles-{Environment.ProcessId}.json");
        }

        return CopilotNexusPaths.NexusSessionProfilesFile;
    }

    private static string GetRuntimeConfigPath()
    {
        var isTestMode = Environment.GetEnvironmentVariable("NEXUS_TEST_MODE") == "1";
        if (isTestMode)
        {
            var testStateRoot = Path.Combine(Path.GetTempPath(), "CopilotNexus", "test-state");
            Directory.CreateDirectory(testStateRoot);
            return Path.Combine(testStateRoot, $"runtime-config-{Environment.ProcessId}.json");
        }

        return CopilotNexusPaths.RuntimeAgentConfigFile;
    }

    /// <summary>
    /// Configures middleware and endpoints on a built WebApplication.
    /// </summary>
    public static WebApplication ConfigureApp(WebApplication app)
    {
        app.UseCors();
        app.MapControllers();
        app.MapHub<SessionHub>("/hubs/session");

        // Health endpoint for status checks
        app.MapGet("/health", (ISessionManager mgr) => Results.Ok(new
        {
            Status = "Running",
            Sessions = mgr.Sessions.Count,
            Models = mgr.AvailableModels.Count,
            Uptime = DateTime.UtcNow.ToString("o"),
        }));

        return app;
    }
}
