namespace CopilotNexus.Core.Services;

using GitHub.Copilot.SDK;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

public class CopilotSessionWrapper : ICopilotSessionWrapper
{
    private readonly CopilotSession _session;
    private readonly ILogger _logger;
    private readonly IDisposable _subscription;
    private bool _disposed;

    public string SessionId { get; }
    public bool IsActive => !_disposed;

    public event EventHandler<SessionOutputEventArgs>? OutputReceived;

    public CopilotSessionWrapper(CopilotSession session, ILogger logger)
    {
        _session = session;
        _logger = logger;
        SessionId = session.SessionId;

        _subscription = _session.On(HandleSessionEvent);
        _logger.LogDebug("Session wrapper {SessionId} initialized", SessionId);
    }

    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Session {SessionId}: sending prompt ({Length} chars)", SessionId, prompt.Length);
        try
        {
            await _session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, timeout: null, cancellationToken);
            _logger.LogDebug("Session {SessionId}: send completed", SessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Session {SessionId}: send failed", SessionId);
            throw;
        }
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Session {SessionId}: aborting", SessionId);
        await _session.AbortAsync(cancellationToken);
    }

    private void HandleSessionEvent(SessionEvent evt)
    {
        try
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    RaiseOutput(delta.Data.DeltaContent, MessageRole.Assistant, OutputKind.Delta);
                    break;

                case AssistantMessageEvent msg:
                    _logger.LogDebug("Session {SessionId}: received full message ({Length} chars)",
                        SessionId, msg.Data.Content?.Length ?? 0);
                    RaiseOutput(msg.Data.Content ?? string.Empty, MessageRole.Assistant, OutputKind.Message);
                    break;

                case SessionIdleEvent:
                    _logger.LogDebug("Session {SessionId}: idle", SessionId);
                    RaiseOutput(string.Empty, MessageRole.System, OutputKind.Idle);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {SessionId}: error handling event {EventType}",
                SessionId, evt.GetType().Name);
        }
    }

    private void RaiseOutput(string content, MessageRole role, OutputKind kind)
    {
        OutputReceived?.Invoke(this, new SessionOutputEventArgs(SessionId, content, role, kind));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Session {SessionId}: disposing", SessionId);
        _subscription.Dispose();

        try
        {
            await _session.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session {SessionId}: error during disposal", SessionId);
        }

        GC.SuppressFinalize(this);
    }
}
