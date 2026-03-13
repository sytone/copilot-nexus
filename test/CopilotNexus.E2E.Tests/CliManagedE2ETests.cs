namespace CopilotNexus.E2E.Tests;

using System.Net;
using Xunit;

[Collection("E2E")]
public class CliManagedE2ETests
{
    [ManagedServiceE2EFact]
    public async Task Cli_StartStatusStop_ManagesServiceLifecycle()
    {
        await TryStopAsync();

        var start = await RunCliAsync($"start --url {E2ETestSettings.ManagedServiceUrl}");
        Assert.Equal(0, start.ExitCode);

        await WaitForHealthAsync(E2ETestSettings.ManagedServiceUrl, TimeSpan.FromSeconds(30));

        var status = await RunCliAsync($"status --url {E2ETestSettings.ManagedServiceUrl}");
        Assert.Equal(0, status.ExitCode);
        var statusText = status.StandardOutput + status.StandardError;
        Assert.True(
            statusText.Contains("responding", StringComparison.OrdinalIgnoreCase) ||
            statusText.Contains("running", StringComparison.OrdinalIgnoreCase),
            $"Expected status output to indicate running/responding. Output: {statusText}");

        var stop = await RunCliAsync("stop");
        Assert.Equal(0, stop.ExitCode);
    }

    [E2EFact]
    public async Task Cli_Help_DoesNotIncludeRemovedUpdateCommand()
    {
        var help = await RunCliAsync("--help");
        Assert.Equal(0, help.ExitCode);
        var output = help.StandardOutput + help.StandardError;
        Assert.DoesNotContain("\n  update", output, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitForHealthAsync(string baseUrl, TimeSpan timeout)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(3) };
        var started = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - started < timeout)
        {
            try
            {
                var response = await http.GetAsync("/health");
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
                // Retry until timeout.
            }
            catch (TaskCanceledException)
            {
                // Retry until timeout.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Service at {baseUrl} did not become healthy within {timeout}.");
    }

    private static async Task<ProcessResult> RunCliAsync(string args, TimeSpan? timeout = null)
    {
        var cliProject = E2ETestSettings.CliProjectPath;
        var commandArgs = $"run --project \"{cliProject}\" -- {args}";
        return await ProcessRunner.RunAsync("dotnet", commandArgs, E2ETestSettings.RepoRoot, timeout ?? TimeSpan.FromMinutes(3));
    }

    private static async Task TryStopAsync()
    {
        _ = await RunCliAsync("stop", TimeSpan.FromSeconds(30));
    }
}
