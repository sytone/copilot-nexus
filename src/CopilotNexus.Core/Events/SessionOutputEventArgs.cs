namespace CopilotNexus.Core.Events;

using CopilotNexus.Core.Models;

public class SessionOutputEventArgs : EventArgs
{
    public string SessionId { get; }
    public string Content { get; }
    public MessageRole Role { get; }
    public OutputKind Kind { get; }

    public SessionOutputEventArgs(string sessionId, string content, MessageRole role, OutputKind kind)
    {
        SessionId = sessionId;
        Content = content;
        Role = role;
        Kind = kind;
    }
}
