namespace CopilotNexus.Core.Models;

public enum OutputKind
{
    /// <summary>Complete message (system notifications, user echo).</summary>
    Message,

    /// <summary>Streaming chunk — content to append to current assistant response.</summary>
    Delta,

    /// <summary>Streaming reasoning/thinking chunk.</summary>
    ReasoningDelta,

    /// <summary>Complete reasoning/thinking message.</summary>
    Reasoning,

    /// <summary>Short runtime status/activity updates (tools, intents, subagents).</summary>
    Activity,

    /// <summary>Stream completed — mark current response as finished.</summary>
    Idle
}
