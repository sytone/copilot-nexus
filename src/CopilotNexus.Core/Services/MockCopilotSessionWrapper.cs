namespace CopilotNexus.Core.Services;

using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Mock session wrapper for UI testing. Simulates streaming responses
/// by breaking a response into word-by-word deltas with short delays.
/// </summary>
public class MockCopilotSessionWrapper : ICopilotSessionWrapper
{
    private readonly string? _model;
    private readonly ILogger _logger;
    private bool _disposed;
    private CancellationTokenSource? _abortCts;

    public string SessionId { get; }
    public bool IsActive => !_disposed;

    public event EventHandler<SessionOutputEventArgs>? OutputReceived;

    public MockCopilotSessionWrapper(string? sessionId, string? model, ILogger logger)
    {
        SessionId = sessionId ?? Guid.NewGuid().ToString("N")[..8];
        _model = model;
        _logger = logger;
    }

    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[MOCK] Session {SessionId}: received prompt '{Prompt}'", SessionId, prompt);

        _abortCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _abortCts.Token;

        var response = GenerateResponse(prompt);
        var words = response.Split(' ');

        try
        {
            foreach (var word in words)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(50, token);

                var delta = word + " ";
                RaiseOutput(delta, MessageRole.Assistant, OutputKind.Delta);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[MOCK] Session {SessionId}: response aborted", SessionId);
            throw;
        }
        finally
        {
            RaiseOutput(string.Empty, MessageRole.System, OutputKind.Idle);
            _abortCts?.Dispose();
            _abortCts = null;
        }
    }

    public Task AbortAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] Session {SessionId}: abort requested", SessionId);
        _abortCts?.Cancel();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionOutputEventArgs>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SessionOutputEventArgs> history = Array.Empty<SessionOutputEventArgs>();
        return Task.FromResult(history);
    }

    private static string GenerateResponse(string prompt)
    {
        return $"[Mock response] You asked: \"{prompt}\". " +
               "This is a simulated response from the mock Copilot session. " +
               "The real SDK would stream an AI-generated response here. " +
               "This mock is used for UI testing and development.";
    }

    private void RaiseOutput(string content, MessageRole role, OutputKind kind)
    {
        OutputReceived?.Invoke(this, new SessionOutputEventArgs(SessionId, content, role, kind));
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _abortCts?.Cancel();
        _abortCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
