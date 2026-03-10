namespace CopilotFamily.App.Services;

using CopilotFamily.Core.Events;
using CopilotFamily.Core.Interfaces;

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

    public string SessionId => _sessionId;
    public bool IsActive { get; set; } = true;

    public event EventHandler<SessionOutputEventArgs>? OutputReceived;

    public NexusSessionProxy(string sessionId)
    {
        _sessionId = sessionId;
    }

    /// <summary>Wire up send/abort delegates from NexusSessionManager.</summary>
    public void SetTransport(Func<string, string, Task> sendFunc, Func<string, Task> abortFunc)
    {
        _sendFunc = sendFunc;
        _abortFunc = abortFunc;
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
