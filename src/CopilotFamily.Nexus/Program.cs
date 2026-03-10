using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Json;
using CopilotFamily.Core.Interfaces;
using CopilotFamily.Nexus;
using Serilog;

// When invoked with no args or via WebApplicationFactory, start the server directly.
// WebApplicationFactory intercepts WebApplication.CreateBuilder(args) to capture the host.
if (args.Length == 0 || !Program.IsCliCommand(args))
{
    var builder = NexusHostBuilder.CreateBuilder(args);
    var app = NexusHostBuilder.ConfigureApp(builder.Build());
    var mgr = app.Services.GetRequiredService<ISessionManager>();
    await mgr.InitializeAsync();
    Log.Information("Nexus service starting");
    await app.RunAsync();
    return 0;
}

// CLI command routing
var urlOption = new Option<string>("--url") { Description = "URL to listen on", DefaultValueFactory = _ => "http://localhost:5280" };

// --- nexus start ---
var startCommand = new Command("start", "Start the Nexus service") { urlOption };
startCommand.SetAction(async (parseResult, ct) =>
{
    var url = parseResult.GetValue(urlOption)!;
    await RunServerAsync(url, ct);
});

// --- nexus status ---
var statusUrlOption = new Option<string>("--url") { Description = "Nexus URL to query", DefaultValueFactory = _ => "http://localhost:5280" };

var statusCommand = new Command("status", "Check the status of a running Nexus instance") { statusUrlOption };
statusCommand.SetAction(async (parseResult, _) =>
{
    var url = parseResult.GetValue(statusUrlOption)!;
    await RunStatusAsync(url);
});

// --- nexus winapp start ---
var nexusUrlOption = new Option<string>("--nexus-url") { Description = "Nexus URL for the app to connect to", DefaultValueFactory = _ => "http://localhost:5280" };
var testModeOption = new Option<bool>("--test-mode") { Description = "Run in test mode with mock services" };

var winappStartCommand = new Command("start", "Launch the Copilot Family desktop application")
{
    nexusUrlOption, testModeOption
};
winappStartCommand.SetAction((parseResult, _) =>
{
    var nexusUrl = parseResult.GetValue(nexusUrlOption)!;
    var testMode = parseResult.GetValue(testModeOption);
    RunWinApp(nexusUrl, testMode);
    return Task.CompletedTask;
});

var winappCommand = new Command("winapp", "Manage the desktop application");
winappCommand.Subcommands.Add(winappStartCommand);

// --- root ---
var rootCommand = new RootCommand("CopilotFamily Nexus — Copilot session management service");
rootCommand.Subcommands.Add(startCommand);
rootCommand.Subcommands.Add(statusCommand);
rootCommand.Subcommands.Add(winappCommand);

return rootCommand.Parse(args).Invoke();

// --- Command implementations ---

static async Task RunServerAsync(string url, CancellationToken ct)
{
    var builder = NexusHostBuilder.CreateBuilder();
    builder.WebHost.UseUrls(url);

    var app = NexusHostBuilder.ConfigureApp(builder.Build());

    var sessionManager = app.Services.GetRequiredService<ISessionManager>();
    await sessionManager.InitializeAsync(ct);

    Log.Information("Nexus service starting on {Url}", url);
    await app.RunAsync(ct);
}

static async Task RunStatusAsync(string url)
{
    var baseUrl = url.TrimEnd('/');
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    try
    {
        var response = await http.GetAsync($"{baseUrl}/health");
        response.EnsureSuccessStatusCode();

        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Console.WriteLine($"Nexus Status: {health?.Status ?? "Unknown"}");
        Console.WriteLine($"  URL:       {baseUrl}");
        Console.WriteLine($"  Sessions:  {health?.Sessions ?? 0}");
        Console.WriteLine($"  Models:    {health?.Models ?? 0}");
        Console.WriteLine($"  Timestamp: {health?.Uptime ?? "N/A"}");
    }
    catch (HttpRequestException)
    {
        Console.Error.WriteLine($"Nexus is not running at {baseUrl}");
        Environment.ExitCode = 1;
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine($"Nexus at {baseUrl} did not respond (timeout)");
        Environment.ExitCode = 1;
    }
}

static void RunWinApp(string nexusUrl, bool testMode)
{
    var nexusDir = AppContext.BaseDirectory;
    var appExe = FindAppExecutable(nexusDir);

    if (appExe == null)
    {
        Console.Error.WriteLine("Could not find CopilotFamily.App.exe");
        Console.Error.WriteLine($"Searched in: {nexusDir}");
        Environment.ExitCode = 1;
        return;
    }

    var arguments = $"--nexus-url {nexusUrl}";
    if (testMode) arguments += " --test-mode";

    Console.WriteLine($"Launching {Path.GetFileName(appExe)}...");
    Console.WriteLine($"  Nexus URL: {nexusUrl}");
    if (testMode) Console.WriteLine("  Mode: test");

    var psi = new ProcessStartInfo
    {
        FileName = appExe,
        Arguments = arguments,
        UseShellExecute = false,
    };

    var process = Process.Start(psi);
    if (process != null)
    {
        Console.WriteLine($"  PID: {process.Id}");
    }
}

static string? FindAppExecutable(string baseDir)
{
    string[] searchPaths =
    [
        Path.Combine(baseDir, "CopilotFamily.App.exe"),
        Path.Combine(baseDir, "..", "CopilotFamily.App", "CopilotFamily.App.exe"),
        Path.Combine(baseDir, "..", "app", "CopilotFamily.App.exe"),
    ];

    var repoRoot = FindRepoRoot(baseDir);
    if (repoRoot != null)
    {
        searchPaths =
        [
            ..searchPaths,
            Path.Combine(repoRoot, "dist", "CopilotFamily.App.exe"),
            Path.Combine(repoRoot, "dist", "staging", "CopilotFamily.App.exe"),
        ];
    }

    return searchPaths.FirstOrDefault(File.Exists);
}

static string? FindRepoRoot(string startDir)
{
    var dir = startDir;
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir, ".git")) ||
            File.Exists(Path.Combine(dir, "CopilotFamily.slnx")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}

record HealthResponse(string? Status, int Sessions, int Models, string? Uptime);

/// <summary>Partial class to expose Program to WebApplicationFactory in tests.</summary>
public partial class Program
{
    internal static bool IsCliCommand(string[] args)
    {
        if (args.Length == 0) return false;
        string[] commands = ["start", "status", "winapp", "--help", "-h", "-?"];
        return commands.Contains(args[0], StringComparer.OrdinalIgnoreCase);
    }
}
