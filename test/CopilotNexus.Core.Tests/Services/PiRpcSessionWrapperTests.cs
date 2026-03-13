namespace CopilotNexus.Core.Tests.Services;

using System.Text;
using CopilotNexus.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class PiRpcSessionWrapperTests
{
    [Fact]
    public async Task SendAsync_MapsPiAutoModelToRuntimeDefault()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var tempRoot = Path.Combine(Path.GetTempPath(), $"nexus-pi-wrapper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var argsPath = Path.Combine(tempRoot, "captured-args.txt");
        var previousArgPath = Environment.GetEnvironmentVariable("NEXUS_TEST_PI_ARGS_FILE");

        try
        {
            var executablePath = await CreateFakePiExecutableAsync(tempRoot, """
                param([Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArgs)

                if (-not [string]::IsNullOrWhiteSpace($env:NEXUS_TEST_PI_ARGS_FILE)) {
                    Set-Content -Path $env:NEXUS_TEST_PI_ARGS_FILE -Value ($RemainingArgs -join ' ') -Encoding utf8
                }

                if ($RemainingArgs -contains '--version') {
                    Write-Output 'pi-test 1.0.0'
                    exit 0
                }

                if (-not ($RemainingArgs -contains '--mode')) {
                    [Console]::Error.WriteLine('unsupported invocation')
                    exit 1
                }

                $modelIndex = [Array]::IndexOf($RemainingArgs, '--model')
                if ($modelIndex -ge 0 -and ($modelIndex + 1) -lt $RemainingArgs.Length) {
                    $model = $RemainingArgs[$modelIndex + 1].Trim('"')
                    if ($model -eq 'pi-auto') {
                        [Console]::Error.WriteLine('Model "pi-auto" not found. Use --list-models to see available models.')
                        exit 1
                    }
                }

                $line = [Console]::In.ReadLine()
                if ([string]::IsNullOrWhiteSpace($line)) {
                    exit 1
                }

                $request = $line | ConvertFrom-Json
                $id = $request.id
                [Console]::Out.WriteLine("{""type"":""response"",""id"":""$id"",""success"":true}")
                [Console]::Out.WriteLine('{"type":"agent_end"}')
                exit 0
                """);

            Environment.SetEnvironmentVariable("NEXUS_TEST_PI_ARGS_FILE", argsPath);
            await using var wrapper = new PiRpcSessionWrapper(
                "session-under-test",
                "pi-auto",
                Directory.GetCurrentDirectory(),
                executablePath,
                NullLogger.Instance);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await wrapper.SendAsync("hello from test", timeout.Token);

            Assert.True(File.Exists(argsPath), "The fake Pi executable did not capture startup arguments.");
            var capturedArgs = await File.ReadAllTextAsync(argsPath);
            Assert.DoesNotContain("--model", capturedArgs, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXUS_TEST_PI_ARGS_FILE", previousArgPath);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SendAsync_ThrowsInvalidOperation_WhenProcessExitsWithoutResponse()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var tempRoot = Path.Combine(Path.GetTempPath(), $"nexus-pi-wrapper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var executablePath = await CreateFakePiExecutableAsync(tempRoot, """
                param([Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArgs)

                if ($RemainingArgs -contains '--version') {
                    Write-Output 'pi-test 1.0.0'
                    exit 0
                }

                [void][Console]::In.ReadLine()
                [Console]::Error.WriteLine('No models available.')
                exit 1
                """);

            await using var wrapper = new PiRpcSessionWrapper(
                "session-under-test",
                null,
                Directory.GetCurrentDirectory(),
                executablePath,
                NullLogger.Instance);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await wrapper.SendAsync("hello from test").WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("No models available", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<string> CreateFakePiExecutableAsync(string tempRoot, string scriptContent)
    {
        var scriptPath = Path.Combine(tempRoot, "fake-pi.ps1");
        await File.WriteAllTextAsync(scriptPath, scriptContent, Encoding.UTF8);

        var executablePath = Path.Combine(tempRoot, "fake-pi.cmd");
        await File.WriteAllTextAsync(executablePath, """
            @echo off
            setlocal
            pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-pi.ps1" %*
            """, Encoding.ASCII);

        return executablePath;
    }
}
