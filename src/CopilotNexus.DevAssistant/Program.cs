using System.CommandLine;
using System.Net.Http.Json;
using CopilotNexus.Core;
using CopilotNexus.DevAssistant;
using Serilog;
using Spectre.Console;

// DevAssistant CLI — watches logs, creates issues, exposes an HTTP action API.
// Usage:
//   nexus dev watch          — start the watcher + HTTP server
//   nexus dev rebuild        — trigger rebuild via HTTP API
//   nexus dev publish        — trigger publish via HTTP API
//   nexus dev restart        — trigger restart via HTTP API
//   nexus dev republish      — trigger rebuild+publish+restart via HTTP API
//   nexus dev status         — query DevAssistant status
//   nexus dev issues         — list open issues

const string DefaultApiUrl = "http://localhost:5290";

var urlOption = new Option<string>("--url") { Description = "DevAssistant API URL", DefaultValueFactory = _ => DefaultApiUrl };

// --- watch ---
var watchCommand = new Command("watch", "Start the DevAssistant watcher and HTTP server") { urlOption };
watchCommand.SetAction(async (parseResult, ct) =>
{
    var apiUrl = parseResult.GetValue(urlOption)!;
    await RunWatchAsync(apiUrl, ct);
});

// --- action shorthands ---
var rebuildCmd = new Command("rebuild", "Trigger a solution rebuild");
rebuildCmd.SetAction(async (_, _) => await SendActionAsync("rebuild"));

var publishCmd = new Command("publish", "Trigger a component publish");
publishCmd.SetAction(async (_, _) => await SendActionAsync("publish"));

var restartCmd = new Command("restart", "Trigger a service restart");
restartCmd.SetAction(async (_, _) => await SendActionAsync("restart"));

var republishCmd = new Command("republish", "Rebuild, publish, and restart");
republishCmd.SetAction(async (_, _) => await SendActionAsync("republish"));

var statusCmd = new Command("status", "Show DevAssistant status");
statusCmd.SetAction(async (_, _) => await SendGetAsync("status"));

var issuesCmd = new Command("issues", "List open issues");
issuesCmd.SetAction(async (_, _) => await SendGetAsync("issues"));

// --- root ---
var rootCommand = new RootCommand("CopilotNexus DevAssistant — log watcher, issue creator, action server")
{
    watchCommand,
    rebuildCmd,
    publishCmd,
    restartCmd,
    republishCmd,
    statusCmd,
    issuesCmd,
};

AnsiConsole.MarkupLine("[bold blue]Copilot Nexus DevAssistant[/]");
return rootCommand.Parse(args).Invoke();

// ─── Watch mode: runs the watcher + HTTP server ─────────────────────────────

static async Task RunWatchAsync(string apiUrl, CancellationToken ct)
{
    var logDir = CopilotNexusPaths.Logs;
    var repoRoot = FindRepoRoot() ?? Directory.GetCurrentDirectory();
    var issueDir = Path.Combine(repoRoot, "docs", "issues");
    var startTime = DateTimeOffset.UtcNow;

    // Configure Serilog for DevAssistant logs
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(logDir, "devassistant-.log"),
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:o} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    // Build the ASP.NET Core host
    var builder = WebApplication.CreateBuilder();
    builder.Host.UseSerilog();
    builder.WebHost.UseUrls(apiUrl);

    builder.Services.AddSingleton(sp =>
        new LogWatcherService(logDir, sp.GetRequiredService<ILogger<LogWatcherService>>()));
    builder.Services.AddSingleton(sp =>
        new IssueManager(issueDir, sp.GetRequiredService<ILogger<IssueManager>>()));
    builder.Services.AddSingleton(sp =>
        new ActionOrchestrator(repoRoot, sp.GetRequiredService<ILogger<ActionOrchestrator>>()));

    var app = builder.Build();

    var watcher = app.Services.GetRequiredService<LogWatcherService>();
    var issueManager = app.Services.GetRequiredService<IssueManager>();

    // Load existing issue hashes to avoid duplicates
    foreach (var hash in issueManager.LoadExistingIssueHashes())
        watcher.RegisterExistingIssueHash(hash);

    // Wire up error detection → issue creation
    watcher.ErrorDetected += entry =>
    {
        var path = issueManager.CreateIssue(entry);
        AnsiConsole.MarkupLine($"[yellow]Issue created:[/] {Markup.Escape(Path.GetFileName(path))} ({entry.Severity})");
    };

    // --- HTTP API endpoints ---

    app.MapGet("/api/status", () =>
    {
        var openIssues = issueManager.ListIssues("open");
        return Results.Ok(new
        {
            watching = true,
            logDirectory = logDir,
            issueDirectory = issueDir,
            openIssues = openIssues.Count,
            uptime = (DateTimeOffset.UtcNow - startTime).ToString(@"d\.hh\:mm\:ss"),
            lastErrorDetected = openIssues
                .OrderByDescending(i => i.Timestamp)
                .FirstOrDefault()?.Timestamp,
        });
    });

    app.MapGet("/api/issues", () =>
    {
        var issues = issueManager.ListIssues();
        return Results.Ok(issues);
    });

    app.MapPost("/api/issues/{fileName}/resolve", (string fileName) =>
    {
        if (issueManager.ResolveIssue(fileName))
            return Results.Ok(new { resolved = true, fileName });
        return Results.NotFound(new { resolved = false, message = "Issue not found or already resolved" });
    });

    var orchestrator = app.Services.GetRequiredService<ActionOrchestrator>();

    app.MapPost("/api/actions/rebuild", async () =>
    {
        var result = await orchestrator.RebuildAsync();
        return Results.Ok(result);
    });

    app.MapPost("/api/actions/publish", async () =>
    {
        var result = await orchestrator.PublishAsync();
        return Results.Ok(result);
    });

    app.MapPost("/api/actions/restart", async () =>
    {
        var result = await orchestrator.RestartAsync();
        return Results.Ok(result);
    });

    app.MapPost("/api/actions/republish", async () =>
    {
        var result = await orchestrator.RepublishAsync();
        return Results.Ok(result);
    });

    // Start log watcher
    watcher.Start();

    // Print banner
    AnsiConsole.Write(new Rule("[bold green]DevAssistant Running[/]").LeftJustified());
    AnsiConsole.MarkupLine($"  Log watch dir: [dim]{Markup.Escape(logDir)}[/]");
    AnsiConsole.MarkupLine($"  Issue dir:     [dim]{Markup.Escape(issueDir)}[/]");
    AnsiConsole.MarkupLine($"  API URL:       [link]{apiUrl}[/]");
    AnsiConsole.MarkupLine($"  Repo root:     [dim]{Markup.Escape(repoRoot)}[/]");
    AnsiConsole.MarkupLine("  Press [bold]Ctrl+C[/] to stop.");
    AnsiConsole.WriteLine();

    await app.RunAsync();
}

// ─── CLI shorthand helpers (send HTTP requests to running DevAssistant) ──────

static async Task SendActionAsync(string action)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    try
    {
        AnsiConsole.MarkupLine($"[blue]Sending {Markup.Escape(action)} request...[/]");
        var response = await http.PostAsync($"{DefaultApiUrl}/api/actions/{action}", null);
        var json = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        var result = System.Text.Json.JsonSerializer.Deserialize<ActionResult>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result != null)
        {
            var color = result.Success ? "green" : "red";
            AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(result.Action)}[/]: {(result.Success ? "Success" : "Failed")}");
            if (!string.IsNullOrWhiteSpace(result.Output))
                AnsiConsole.WriteLine(result.Output);
        }
    }
    catch (HttpRequestException ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to connect to DevAssistant.[/] Is it running? ({Markup.Escape(ex.Message)})");
        Environment.ExitCode = 1;
    }
}

static async Task SendGetAsync(string endpoint)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    try
    {
        var response = await http.GetAsync($"{DefaultApiUrl}/api/{endpoint}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        // Pretty-print JSON
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var formatted = System.Text.Json.JsonSerializer.Serialize(doc,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        AnsiConsole.WriteLine(formatted);
    }
    catch (HttpRequestException ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to connect to DevAssistant.[/] Is it running? ({Markup.Escape(ex.Message)})");
        Environment.ExitCode = 1;
    }
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir, "CopilotNexus.slnx")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName;
    }

    return null;
}
