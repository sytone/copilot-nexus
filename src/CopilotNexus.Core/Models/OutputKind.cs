namespace CopilotNexus.Core.Models;

public enum OutputKind
{
    /// <summary>Complete message (system notifications, user echo).</summary>
    Message,

    /// <summary>Streaming chunk — content to append to current assistant response.</summary>
    Delta,

    /// <summary>Stream completed — mark current response as finished.</summary>
    Idle
}
