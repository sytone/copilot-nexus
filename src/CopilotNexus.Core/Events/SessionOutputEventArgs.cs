namespace CopilotNexus.Core.Events;

using CopilotNexus.Core.Models;

public class SessionOutputEventArgs : EventArgs
{
    public string SessionId { get; }
    public string Content { get; }
    public MessageRole Role { get; }
    public OutputKind Kind { get; }
    public string? CorrelationId { get; }

    public SessionOutputEventArgs(
        string sessionId,
        string content,
        MessageRole role,
        OutputKind kind,
        string? correlationId = null)
    {
        SessionId = sessionId;
        Content = content;
        Role = role;
        Kind = kind;
        CorrelationId = correlationId;
    }
}
