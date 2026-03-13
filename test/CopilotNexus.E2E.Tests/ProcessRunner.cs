namespace CopilotNexus.E2E.Tests;

using System.Diagnostics;
using System.Text;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromMinutes(3);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? E2ETestSettings.RepoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var output = new StringBuilder();
        var error = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data != null)
                output.AppendLine(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data != null)
                error.AppendLine(eventArgs.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout.Value);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process exited between HasExited check and Kill call.
                }
            }

            throw new TimeoutException($"Process timed out after {timeout}: {fileName} {arguments}");
        }

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }
}
