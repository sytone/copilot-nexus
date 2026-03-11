using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Json;
using CopilotNexus.Core;
using Spectre.Console;

// CLI command routing — all management commands for CopilotNexus.
// The Service (ASP.NET Core) is a separate executable; this CLI starts/stops it.

var urlOption = new Option<string>("--url") { Description = "URL for the service to listen on", DefaultValueFactory = _ => "http://localhost:5280" };

// --- nexus start ---
var startCommand = new Command("start", "Start the Nexus service as a background process") { urlOption };
startCommand.SetAction(async (parseResult, _) =>
{
    var url = parseResult.GetValue(urlOption)!;
    await Task.CompletedTask;
    CliCommands.RunStart(url);
});

// --- nexus stop ---
var stopCommand = new Command("stop", "Stop a running Nexus service");
stopCommand.SetAction((_, _) => { CliCommands.RunStop(); return Task.CompletedTask; });

// --- nexus status ---
var statusUrlOption = new Option<string>("--url") { Description = "Nexus URL to query", DefaultValueFactory = _ => "http://localhost:5280" };
var statusCommand = new Command("status", "Check the status of a running Nexus service") { statusUrlOption };
statusCommand.SetAction(async (parseResult, _) =>
{
    var url = parseResult.GetValue(statusUrlOption)!;
    await CliCommands.RunStatusAsync(url);
});

// --- nexus install ---
var installCommand = new Command("install", "Install Nexus and App to the local install directory");
installCommand.SetAction(async (_, _) => { await CliCommands.RunInstallAsync(); });

// --- nexus build ---
var buildConfigOption = new Option<string>("--configuration", "-c") { Description = "Build configuration", DefaultValueFactory = _ => "Release" };
var buildCommand = new Command("build", "Build the solution from the repository") { buildConfigOption };
buildCommand.SetAction(async (parseResult, _) =>
{
    var config = parseResult.GetValue(buildConfigOption)!;
    await CliCommands.RunBuildAsync(config);
});

// --- nexus update ---
var updateComponentOption = new Option<string>("--component") { Description = "Component to update (nexus, app, or both)", DefaultValueFactory = _ => "both" };
var updateCommand = new Command("update", "Update a component from staging") { updateComponentOption };
updateCommand.SetAction(async (parseResult, _) =>
{
    var component = parseResult.GetValue(updateComponentOption)!;
    await CliCommands.RunUpdateAsync(component);
});

// --- nexus publish ---
var publishComponentOption = new Option<string>("--component") { Description = "Component to publish (nexus, app, or both)", DefaultValueFactory = _ => "both" };
var publishCommand = new Command("publish", "Build and publish components to staging") { publishComponentOption };
publishCommand.SetAction(async (parseResult, _) =>
{
    var component = parseResult.GetValue(publishComponentOption)!;
    await CliCommands.RunPublishAsync(component);
});

// --- nexus winapp ---
var nexusUrlOption = new Option<string>("--nexus-url") { Description = "Nexus URL for the app to connect to", DefaultValueFactory = _ => "http://localhost:5280" };
var testModeOption = new Option<bool>("--test-mode") { Description = "Run in test mode with mock services" };

var winappStartCommand = new Command("start", "Launch the Copilot Nexus desktop application")
{
    nexusUrlOption, testModeOption
};
winappStartCommand.SetAction((parseResult, _) =>
{
    var nexusUrl = parseResult.GetValue(nexusUrlOption)!;
    var testMode = parseResult.GetValue(testModeOption);
    CliCommands.RunWinApp(nexusUrl, testMode);
    return Task.CompletedTask;
});

var winappCommand = new Command("winapp", "Manage the desktop application");
winappCommand.Subcommands.Add(winappStartCommand);

// --- root ---
var rootCommand = new RootCommand("CopilotNexus — Copilot session management CLI");
rootCommand.Subcommands.Add(startCommand);
rootCommand.Subcommands.Add(stopCommand);
rootCommand.Subcommands.Add(statusCommand);
rootCommand.Subcommands.Add(buildCommand);
rootCommand.Subcommands.Add(installCommand);
rootCommand.Subcommands.Add(updateCommand);
rootCommand.Subcommands.Add(publishCommand);
rootCommand.Subcommands.Add(winappCommand);

return rootCommand.Parse(args).Invoke();
