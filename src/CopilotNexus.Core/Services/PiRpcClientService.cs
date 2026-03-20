namespace CopilotNexus.Core.Services;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Agent client service backed by Pi RPC mode (<c>pi --mode rpc</c>).
/// </summary>
public sealed class PiRpcClientService : IAgentClientService
{
    private const string ModelDiscoveryRequestId = "nexus-model-discovery";
    private static readonly TimeSpan ModelCacheTtl = TimeSpan.FromSeconds(30);
    private readonly ILogger<PiRpcClientService> _logger;
    private readonly string _piExecutablePath;
    private readonly string _piSessionRoot;
    private readonly ConcurrentDictionary<string, PiRpcSessionWrapper> _sessions = new();
    private readonly SemaphoreSlim _modelCacheLock = new(1, 1);
    private IReadOnlyList<ModelInfo> _cachedModels = [];
    private DateTimeOffset _cachedModelsExpiresUtc = DateTimeOffset.MinValue;
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
        _cachedModels = [];
        _cachedModelsExpiresUtc = DateTimeOffset.MinValue;
        _started = false;
        _logger.LogInformation("Pi RPC client stopped");
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        return ListModelsInternalAsync(cancellationToken);
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

    private async Task<IReadOnlyList<ModelInfo>> ListModelsInternalAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken);

        if (TryGetCachedModels(out var cached))
            return cached;

        await _modelCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCachedModels(out cached))
                return cached;

            IReadOnlyList<ModelInfo> resolvedModels;
            var models = await QueryModelsViaRpcAsync(cancellationToken);
            if (models.Count > 0)
            {
                _logger.LogInformation("Loaded {Count} Pi runtime models", models.Count);
                resolvedModels = models;
            }
            else
            {
                _logger.LogWarning("Pi runtime returned no models; falling back to default model catalog.");
                resolvedModels = GetFallbackModels();
            }

            return UpdateModelCache(resolvedModels);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Pi model catalog via RPC. Falling back to default model catalog.");
            return UpdateModelCache(GetFallbackModels());
        }
        finally
        {
            _modelCacheLock.Release();
        }
    }

    private async Task<IReadOnlyList<ModelInfo>> QueryModelsViaRpcAsync(CancellationToken cancellationToken)
    {
        Process? process;
        var psi = new ProcessStartInfo
        {
            FileName = _piExecutablePath,
            Arguments = "--mode rpc --no-session",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw CreatePiNotFoundException(ex);
        }

        if (process == null)
            throw CreatePiNotFoundException();

        using (process)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    id = ModelDiscoveryRequestId,
                    type = "get_available_models",
                });
                await process.StandardInput.WriteLineAsync(payload);
                await process.StandardInput.FlushAsync();

                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(timeoutCts.Token);
                    if (line == null)
                        break;
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("type", out var typeElement) ||
                        !string.Equals(typeElement.GetString(), "response", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!root.TryGetProperty("id", out var idElement) ||
                        !string.Equals(idElement.GetString(), ModelDiscoveryRequestId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!root.TryGetProperty("success", out var successElement) || !successElement.GetBoolean())
                    {
                        var errorMessage = root.TryGetProperty("error", out var errorElement)
                            ? errorElement.GetString()
                            : "unknown error";
                        throw new InvalidOperationException($"Pi model discovery failed: {errorMessage}");
                    }

                    if (!root.TryGetProperty("data", out var dataElement) ||
                        !dataElement.TryGetProperty("models", out var modelsElement) ||
                        modelsElement.ValueKind != JsonValueKind.Array)
                    {
                        return [];
                    }

                    return ParseRpcModels(modelsElement);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"Pi model discovery via '{_piExecutablePath}' timed out after 8 seconds.");
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch
                {
                    // Best-effort close for shutdown.
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }

            var stderr = (await stderrTask).Trim();
            throw new InvalidOperationException(
                $"Pi model discovery returned no response. Stderr: {stderr}");
        }
    }

    private static IReadOnlyList<ModelInfo> ParseRpcModels(JsonElement modelsElement)
    {
        var parsedModels = new List<ModelInfo>();
        foreach (var modelElement in modelsElement.EnumerateArray())
        {
            var rawId = modelElement.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(rawId))
                continue;

            var provider = modelElement.TryGetProperty("provider", out var providerElement)
                ? providerElement.GetString()
                : null;
            var displayName = modelElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) ? rawId : displayName;
            if (!string.IsNullOrWhiteSpace(provider) &&
                !resolvedDisplayName.Contains(provider, StringComparison.OrdinalIgnoreCase))
            {
                resolvedDisplayName = $"{resolvedDisplayName} ({provider})";
            }

            var capabilities = new List<string> { "streaming" };
            if (modelElement.TryGetProperty("reasoning", out var reasoningElement) && reasoningElement.GetBoolean())
                capabilities.Add("reasoning");

            if (!string.IsNullOrWhiteSpace(provider))
                capabilities.Add($"provider:{provider}");

            if (modelElement.TryGetProperty("api", out var apiElement))
            {
                var api = apiElement.GetString();
                if (!string.IsNullOrWhiteSpace(api))
                    capabilities.Add($"api:{api}");
            }

            if (modelElement.TryGetProperty("contextWindow", out var contextWindowElement) &&
                contextWindowElement.TryGetInt64(out var contextWindow))
            {
                capabilities.Add($"context-window:{contextWindow}");
            }

            if (modelElement.TryGetProperty("maxTokens", out var maxTokensElement) &&
                maxTokensElement.TryGetInt64(out var maxTokens))
            {
                capabilities.Add($"max-tokens:{maxTokens}");
            }

            if (modelElement.TryGetProperty("cost", out var costElement) &&
                costElement.ValueKind == JsonValueKind.Object)
            {
                var costDescription = BuildCostDescription(costElement);
                if (!string.IsNullOrWhiteSpace(costDescription))
                    capabilities.Add($"cost:{costDescription}");
            }

            parsedModels.Add(new ModelInfo
            {
                ModelId = ComposeModelId(provider, rawId),
                Name = resolvedDisplayName,
                Capabilities = capabilities,
            });
        }

        return parsedModels
            .GroupBy(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ComposeModelId(string? provider, string rawModelId)
    {
        if (rawModelId.Contains('/', StringComparison.Ordinal))
            return rawModelId;

        return string.IsNullOrWhiteSpace(provider)
            ? rawModelId
            : $"{provider}/{rawModelId}";
    }

    private static string? BuildCostDescription(JsonElement costElement)
    {
        var parts = new List<string>();
        if (costElement.TryGetProperty("input", out var input) && input.TryGetDecimal(out var inputValue))
            parts.Add($"in={inputValue:0.###}");
        if (costElement.TryGetProperty("output", out var output) && output.TryGetDecimal(out var outputValue))
            parts.Add($"out={outputValue:0.###}");

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static IReadOnlyList<ModelInfo> GetFallbackModels() =>
    [
        new()
        {
            ModelId = "pi-auto",
            Name = "Pi Auto (RPC)",
            Capabilities = ["streaming", "reasoning", "tools", "steering", "follow-up"],
        },
    ];

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

    private bool TryGetCachedModels(out IReadOnlyList<ModelInfo> models)
    {
        if (_cachedModels.Count > 0 && DateTimeOffset.UtcNow < _cachedModelsExpiresUtc)
        {
            models = _cachedModels;
            return true;
        }

        models = [];
        return false;
    }

    private IReadOnlyList<ModelInfo> UpdateModelCache(IReadOnlyList<ModelInfo> models)
    {
        _cachedModels = models.ToList().AsReadOnly();
        _cachedModelsExpiresUtc = DateTimeOffset.UtcNow + ModelCacheTtl;
        return _cachedModels;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PiRpcClientService));
    }
}
