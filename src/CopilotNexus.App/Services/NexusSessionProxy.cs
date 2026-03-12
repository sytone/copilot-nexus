namespace CopilotNexus.App.Services;

using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;

/// <summary>
/// Proxy session wrapper for Nexus-backed sessions.
/// Receives output events from the NexusSessionManager (via SignalR)
/// and raises them to the ViewModel. Send/Abort delegate to Nexus via hub.
/// </summary>
public class NexusSessionProxy : ICopilotSessionWrapper
{
    private readonly string _sessionId;
    private Func<string, string, Task>? _sendFunc;
    private Func<string, Task>? _abortFunc;
    private Func<string, CancellationToken, Task<IReadOnlyList<SessionOutputEventArgs>>>? _historyFunc;

    public string SessionId => _sessionId;
    public bool IsActive { get; set; } = true;

    public event EventHandler<SessionOutputEventArgs>? OutputReceived;

    public NexusSessionProxy(string sessionId)
    {
        _sessionId = sessionId;
    }

    /// <summary>Wire up send/abort delegates from NexusSessionManager.</summary>
    public void SetTransport(
        Func<string, string, Task> sendFunc,
        Func<string, Task> abortFunc,
        Func<string, CancellationToken, Task<IReadOnlyList<SessionOutputEventArgs>>>? historyFunc = null)
    {
        _sendFunc = sendFunc;
        _abortFunc = abortFunc;
        _historyFunc = historyFunc;
    }

    public async Task SendAsync(string input, CancellationToken cancellationToken = default)
    {
        if (_sendFunc != null)
        {
            await _sendFunc(_sessionId, input);
        }
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        if (_abortFunc != null)
        {
            await _abortFunc(_sessionId);
        }
    }

    public async Task<IReadOnlyList<SessionOutputEventArgs>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (_historyFunc == null)
            return Array.Empty<SessionOutputEventArgs>();

        return await _historyFunc(_sessionId, cancellationToken);
    }

    /// <summary>Called by NexusSessionManager when SignalR output arrives.</summary>
    public void RaiseOutput(SessionOutputEventArgs e)
    {
        OutputReceived?.Invoke(this, e);
    }

    public ValueTask DisposeAsync()
    {
        IsActive = false;
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
