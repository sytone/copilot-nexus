using CopilotFamily.Core.Interfaces;
using CopilotFamily.Core.Services;
using CopilotFamily.Nexus.Hubs;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotFamily", "logs", "nexus-.log"),
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

// CORS for local development (Avalonia app connects from localhost)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true));
});

var app = builder.Build();

// Initialize session manager
var sessionManager = app.Services.GetRequiredService<ISessionManager>();
await sessionManager.InitializeAsync();

app.UseCors();
app.MapControllers();
app.MapHub<SessionHub>("/hubs/session");

Log.Information("Nexus service started on {Urls}", string.Join(", ", app.Urls));
app.Run();

/// <summary>Partial class to expose Program to WebApplicationFactory in tests.</summary>
public partial class Program { }
