namespace CopilotNexus.Core.Services;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Session wrapper backed by a Pi RPC subprocess.
/// </summary>
public sealed class PiRpcSessionWrapper : ICopilotSessionWrapper
{
    private const string RuntimeAutoModelId = "pi-auto";
    private readonly string? _model;
    private readonly string? _workingDirectory;
    private readonly string _piExecutablePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly List<SessionOutputEventArgs> _history = new();
    private readonly object _historyLock = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingResponses = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _readerCts = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutReaderTask;
    private Task? _stderrReaderTask;
    private TaskCompletionSource<bool>? _activePromptCompletion;
    private bool _seenAssistantDelta;
    private bool _disposed;
    private int _terminationSignaled;
    private string? _lastStderrLine;
    private Exception? _terminationException;

    public string SessionId { get; }
    public bool IsActive => !_disposed;
    public event EventHandler<SessionOutputEventArgs>? OutputReceived;

    public PiRpcSessionWrapper(
        string sessionId,
        string? model,
        string? workingDirectory,
        string piExecutablePath,
        ILogger logger)
    {
        SessionId = sessionId;
        _model = model;
        _workingDirectory = workingDirectory;
        _piExecutablePath = piExecutablePath;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_process != null)
            return Task.CompletedTask;

        var arguments = BuildArguments(_model);
        var workingDirectory = ResolveWorkingDirectory(_workingDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = _piExecutablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            _process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start Pi RPC process.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start Pi executable '{_piExecutablePath}'. Set NEXUS_PI_EXECUTABLE if pi is not in PATH.", ex);
        }

        _stdin = _process.StandardInput;
        _stdin.AutoFlush = true;
        _stdoutReaderTask = Task.Run(() => ReadStdoutLoopAsync(_readerCts.Token), _readerCts.Token);
        _stderrReaderTask = Task.Run(() => ReadStderrLoopAsync(_readerCts.Token), _readerCts.Token);

        _logger.LogInformation("Pi RPC session {SessionId} started (cwd={WorkingDirectory}, model={Model})",
            SessionId, workingDirectory, _model ?? "default");

        return Task.CompletedTask;
    }

    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await InitializeAsync(cancellationToken);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            _seenAssistantDelta = false;
            _activePromptCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await SendCommandAsync(new Dictionary<string, object?>
            {
                ["type"] = "prompt",
                ["message"] = prompt,
            }, cancellationToken);

            await _activePromptCompletion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _activePromptCompletion = null;
            _sendLock.Release();
        }
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _stdin == null)
            return;

        await SendCommandAsync(new Dictionary<string, object?>
        {
            ["type"] = "abort",
        }, cancellationToken);

        _activePromptCompletion?.TrySetCanceled(cancellationToken);
    }

    public Task<IReadOnlyList<SessionOutputEventArgs>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        lock (_historyLock)
        {
            return Task.FromResult<IReadOnlyList<SessionOutputEventArgs>>(_history.ToList());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _readerCts.Cancel();

        foreach (var waiter in _pendingResponses.Values)
            waiter.TrySetCanceled();
        _pendingResponses.Clear();

        _activePromptCompletion?.TrySetCanceled();

        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to kill Pi RPC process for session {SessionId}", SessionId);
            }
        }

        if (_stdoutReaderTask != null)
            await _stdoutReaderTask;
        if (_stderrReaderTask != null)
            await _stderrReaderTask;

        _stdin?.Dispose();
        _process?.Dispose();
        _readerCts.Dispose();
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SendCommandAsync(Dictionary<string, object?> command, CancellationToken cancellationToken)
    {
        if (_stdin == null)
            throw new InvalidOperationException("Pi RPC session is not initialized.");
        if (_terminationException is Exception terminatedException)
            throw terminatedException;

        var id = Guid.NewGuid().ToString("N");
        command["id"] = id;
        var responseWaiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses[id] = responseWaiter;
        if (_terminationException is Exception terminatedAfterRegistration)
        {
            _pendingResponses.TryRemove(id, out _);
            throw terminatedAfterRegistration;
        }

        try
        {
            var payload = JsonSerializer.Serialize(command, _jsonOptions);
            await _stdin.WriteLineAsync(payload);
        }
        catch
        {
            _pendingResponses.TryRemove(id, out _);
            throw;
        }

        string responseLine;
        using (var registration = cancellationToken.Register(() => responseWaiter.TrySetCanceled(cancellationToken)))
        {
            try
            {
                responseLine = await responseWaiter.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _pendingResponses.TryRemove(id, out _);
            }
        }

        using var responseDoc = JsonDocument.Parse(responseLine);
        var responseRoot = responseDoc.RootElement;

        var success = responseRoot.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
        if (success)
            return;

        var errorMessage = responseRoot.TryGetProperty("error", out var errorElement)
            ? errorElement.GetString()
            : null;
        throw new InvalidOperationException(errorMessage ?? $"Pi RPC command '{command["type"]}' failed.");
    }

    private async Task ReadStdoutLoopAsync(CancellationToken cancellationToken)
    {
        if (_process == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync();
                if (line == null)
                {
                    SignalProcessTermination("Pi RPC process exited before returning an RPC response");
                    break;
                }

                ProcessRpcLine(line);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Pi RPC stdout loop failed for session {SessionId}", SessionId);
            SignalProcessTermination("Pi RPC stdout loop failed", ex);
        }
    }

    private async Task ReadStderrLoopAsync(CancellationToken cancellationToken)
    {
        if (_process == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _process.StandardError.ReadLineAsync();
                if (line == null)
                    break;

                if (!string.IsNullOrWhiteSpace(line))
                {
                    _lastStderrLine = line;
                    _logger.LogDebug("Pi RPC [{SessionId}] stderr: {Line}", SessionId, line);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Pi RPC stderr loop ended for session {SessionId}", SessionId);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && _process is { HasExited: true })
            {
                SignalProcessTermination("Pi RPC process exited before returning an RPC response");
            }
        }
    }

    private void ProcessRpcLine(string line)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
                return;

            var type = typeElement.GetString();
            if (string.IsNullOrWhiteSpace(type))
                return;

            if (string.Equals(type, "response", StringComparison.OrdinalIgnoreCase))
            {
                var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id) && _pendingResponses.TryRemove(id, out var waiter))
                    waiter.TrySetResult(line);
                return;
            }

            HandleEvent(type, root);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse Pi RPC line for session {SessionId}: {Line}", SessionId, line);
        }
        finally
        {
            document?.Dispose();
        }
    }

    private void HandleEvent(string type, JsonElement root)
    {
        switch (type)
        {
            case "message_update":
                HandleMessageUpdate(root);
                break;

            case "message_end":
                HandleMessageEnd(root);
                break;

            case "tool_execution_start":
            {
                var toolName = root.TryGetProperty("toolName", out var toolNameElement)
                    ? toolNameElement.GetString()
                    : null;
                var content = string.IsNullOrWhiteSpace(toolName) ? "Tool started" : $"Tool started: {toolName}";
                Emit(content, MessageRole.System, OutputKind.Activity, includeInHistory: true);
                break;
            }

            case "tool_execution_update":
            {
                var partial = root.TryGetProperty("partialResult", out var partialElement)
                    ? partialElement.ToString()
                    : "Tool progress update";
                Emit($"Tool progress: {partial}", MessageRole.System, OutputKind.Activity, includeInHistory: true);
                break;
            }

            case "tool_execution_end":
            {
                var isError = root.TryGetProperty("isError", out var isErrorElement) && isErrorElement.GetBoolean();
                var result = root.TryGetProperty("result", out var resultElement) ? resultElement.ToString() : null;
                var content = isError
                    ? $"Tool failed: {result}"
                    : "Tool completed";
                Emit(content, MessageRole.System, OutputKind.Activity, includeInHistory: true);
                break;
            }

            case "auto_retry_start":
                Emit("Auto retry started", MessageRole.System, OutputKind.Activity, includeInHistory: true);
                break;

            case "auto_retry_end":
                Emit("Auto retry completed", MessageRole.System, OutputKind.Activity, includeInHistory: true);
                break;

            case "auto_compaction_start":
                Emit("Auto compaction started", MessageRole.System, OutputKind.Activity, includeInHistory: true);
                break;

            case "auto_compaction_end":
                Emit("Auto compaction completed", MessageRole.System, OutputKind.Activity, includeInHistory: true);
                break;

            case "extension_error":
                Emit("Extension error reported by Pi runtime", MessageRole.System, OutputKind.Activity, includeInHistory: true);
                break;

            case "agent_end":
                Emit(string.Empty, MessageRole.System, OutputKind.Idle, includeInHistory: false);
                _activePromptCompletion?.TrySetResult(true);
                break;
        }
    }

    private void HandleMessageUpdate(JsonElement root)
    {
        if (!root.TryGetProperty("assistantMessageEvent", out var eventElement))
            return;
        if (!eventElement.TryGetProperty("type", out var typeElement))
            return;

        var updateType = typeElement.GetString();
        switch (updateType)
        {
            case "text_delta":
            {
                var delta = eventElement.TryGetProperty("delta", out var deltaElement)
                    ? deltaElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(delta))
                    return;

                _seenAssistantDelta = true;
                Emit(delta, MessageRole.Assistant, OutputKind.Delta, includeInHistory: false);
                break;
            }

            case "thinking_delta":
            {
                var delta = eventElement.TryGetProperty("delta", out var deltaElement)
                    ? deltaElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(delta))
                    return;

                Emit(delta, MessageRole.System, OutputKind.ReasoningDelta, includeInHistory: false);
                break;
            }
        }
    }

    private void HandleMessageEnd(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var messageElement))
            return;

        var role = messageElement.TryGetProperty("role", out var roleElement) ? roleElement.GetString() : null;
        if (!string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            return;

        if (_seenAssistantDelta)
            return;

        var text = ExtractMessageText(messageElement);
        if (string.IsNullOrWhiteSpace(text))
            return;

        Emit(text, MessageRole.Assistant, OutputKind.Message, includeInHistory: true);
    }

    private static string? ExtractMessageText(JsonElement messageElement)
    {
        if (!messageElement.TryGetProperty("content", out var contentElement))
            return null;

        return contentElement.ValueKind switch
        {
            JsonValueKind.String => contentElement.GetString(),
            JsonValueKind.Array => string.Join(
                string.Empty,
                contentElement.EnumerateArray()
                    .Select(item => item.TryGetProperty("text", out var textElement) ? textElement.GetString() : null)
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => contentElement.ToString(),
        };
    }

    private void Emit(string content, MessageRole role, OutputKind kind, bool includeInHistory)
    {
        var output = new SessionOutputEventArgs(SessionId, content, role, kind);
        if (includeInHistory)
        {
            lock (_historyLock)
            {
                _history.Add(output);
            }
        }

        OutputReceived?.Invoke(this, output);
    }

    private static string BuildArguments(string? model)
    {
        var normalizedModel = model?.Trim();
        if (IsRuntimeAutoModel(normalizedModel))
            return "--mode rpc --no-session";

        return $"--mode rpc --no-session --model \"{normalizedModel!.Replace("\"", "\\\"")}\"";
    }

    private static bool IsRuntimeAutoModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return true;

        return string.Equals(model.Trim(), RuntimeAutoModelId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(model.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWorkingDirectory(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return Directory.GetCurrentDirectory();

        return Directory.Exists(workingDirectory)
            ? workingDirectory
            : Directory.GetCurrentDirectory();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PiRpcSessionWrapper));
    }

    private void SignalProcessTermination(string reason, Exception? innerException = null)
    {
        if (_readerCts.IsCancellationRequested)
            return;

        var detail = string.IsNullOrWhiteSpace(_lastStderrLine)
            ? reason
            : $"{reason}. Last stderr: {_lastStderrLine}";
        var failure = new InvalidOperationException(detail, innerException);
        _terminationException ??= failure;

        if (Interlocked.Exchange(ref _terminationSignaled, 1) != 0)
            return;

        foreach (var waiter in _pendingResponses.Values)
            waiter.TrySetException(failure);
        _pendingResponses.Clear();

        _activePromptCompletion?.TrySetException(failure);
    }
}
