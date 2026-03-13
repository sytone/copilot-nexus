namespace CopilotNexus.Core.Services;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Agent client service backed by Pi RPC mode (<c>pi --mode rpc</c>).
/// </summary>
public sealed class PiRpcClientService : IAgentClientService
{
    private readonly ILogger<PiRpcClientService> _logger;
    private readonly string _piExecutablePath;
    private readonly string _piSessionRoot;
    private readonly ConcurrentDictionary<string, PiRpcSessionWrapper> _sessions = new();
    private bool _started;
    private bool _disposed;

    public bool IsConnected => _started;

    public PiRpcClientService(ILogger<PiRpcClientService> logger)
    {
        _logger = logger;
        _piExecutablePath = Environment.GetEnvironmentVariable("NEXUS_PI_EXECUTABLE")
            ?? (OperatingSystem.IsWindows() ? "pi.cmd" : "pi");
        _piSessionRoot = Path.Combine(CopilotNexusPaths.StateRoot, "pi-rpc-sessions");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await ValidatePiExecutableAsync(cancellationToken);
        Directory.CreateDirectory(_piSessionRoot);
        _started = true;
        _logger.LogInformation("Pi RPC client started (executable={Executable})", _piExecutablePath);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started)
            return;

        foreach (var (sessionId, session) in _sessions.ToArray())
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose Pi RPC session {SessionId}", sessionId);
            }
        }

        _sessions.Clear();
        _started = false;
        _logger.LogInformation("Pi RPC client stopped");
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        IReadOnlyList<ModelInfo> models =
        [
            new()
            {
                ModelId = "pi-auto",
                Name = "Pi Auto (RPC)",
                Capabilities = ["streaming", "reasoning", "tools", "steering", "follow-up"],
            },
        ];

        return Task.FromResult(models);
    }

    public async Task<ICopilotSessionWrapper> CreateSessionAsync(
        string? sessionId = null,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken);

        var resolvedSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? $"pi-rpc-{Guid.NewGuid():N}"[..15]
            : sessionId;

        config ??= new SessionConfiguration();
        var wrapper = new PiRpcSessionWrapper(
            resolvedSessionId,
            config.Model,
            config.WorkingDirectory,
            _piExecutablePath,
            _logger);

        await wrapper.InitializeAsync(cancellationToken);
        _sessions[resolvedSessionId] = wrapper;
        return wrapper;
    }

    public async Task<ICopilotSessionWrapper> ResumeSessionAsync(
        string sessionId,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken);

        if (_sessions.TryGetValue(sessionId, out var existing))
            return existing;

        config ??= new SessionConfiguration();
        var wrapper = new PiRpcSessionWrapper(
            sessionId,
            config.Model,
            config.WorkingDirectory,
            _piExecutablePath,
            _logger);

        await wrapper.InitializeAsync(cancellationToken);
        _sessions[sessionId] = wrapper;
        return wrapper;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(sessionId, out var wrapper))
            await wrapper.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_started)
            return;

        await StartAsync(cancellationToken);
    }

    private async Task ValidatePiExecutableAsync(CancellationToken cancellationToken)
    {
        Process? process;
        var psi = new ProcessStartInfo
        {
            FileName = _piExecutablePath,
            Arguments = "--version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw CreatePiNotFoundException(ex);
        }
        catch (InvalidOperationException ex)
        {
            throw CreatePiNotFoundException(ex);
        }

        if (process == null)
            throw CreatePiNotFoundException();

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw new InvalidOperationException(
                    $"Pi executable '{_piExecutablePath}' did not respond to '--version' within 5 seconds.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new InvalidOperationException(
                    $"Pi executable '{_piExecutablePath}' failed '--version' validation (exit code {process.ExitCode}). " +
                    $"Output: {detail.Trim()}");
            }
        }
    }

    private InvalidOperationException CreatePiNotFoundException(Exception? innerException = null)
        => new(
            $"Failed to start Pi executable '{_piExecutablePath}'. Set NEXUS_PI_EXECUTABLE if pi is not in PATH.",
            innerException);

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PiRpcClientService));
    }
}
