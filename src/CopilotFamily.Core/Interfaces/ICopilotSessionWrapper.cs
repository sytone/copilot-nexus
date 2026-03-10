namespace CopilotFamily.Core.Interfaces;

using CopilotFamily.Core.Events;

/// <summary>
/// Wraps a single CopilotSession (one per tab) with event-based streaming output.
/// </summary>
public interface ICopilotSessionWrapper : IAsyncDisposable
{
    string SessionId { get; }
    bool IsActive { get; }

    /// <summary>
    /// Fired for each output event: full messages, streaming deltas, and idle notifications.
    /// </summary>
    event EventHandler<SessionOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Sends a prompt and waits for the complete response.
    /// Streaming delta events fire via OutputReceived during the wait.
    /// </summary>
    Task SendAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts the current in-flight message processing.
    /// </summary>
    Task AbortAsync(CancellationToken cancellationToken = default);
}
