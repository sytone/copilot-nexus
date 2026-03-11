namespace CopilotNexus.Service;

using CopilotNexus.Core.Interfaces;
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
    {
        var builder = WebApplication.CreateBuilder(args ?? []);

        // Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CopilotNexus", "logs", "nexus-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        builder.Host.UseSerilog();

        // Services
        builder.Services.AddSignalR();
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSingleton<ICopilotClientService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<CopilotClientService>>();
            return new CopilotClientService(logger);
        });

        builder.Services.AddSingleton<ISessionManager>(sp =>
        {
            var client = sp.GetRequiredService<ICopilotClientService>();
            var logger = sp.GetRequiredService<ILogger<SessionManager>>();
            return new SessionManager(client, logger);
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
