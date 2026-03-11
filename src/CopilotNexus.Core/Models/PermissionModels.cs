namespace CopilotNexus.Core.Models;

/// <summary>
/// Represents a permission request from the agent to use a tool.
/// Surfaced to the UI in interactive mode so the user can approve or deny.
/// </summary>
public class ToolPermissionRequest
{
    /// <summary>Name of the tool the agent wants to use (e.g., "edit_file", "run_command").</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Description or arguments preview for the tool call.</summary>
    public string Details { get; set; } = string.Empty;
}

/// <summary>
/// The user's decision on a permission request.
/// </summary>
public enum PermissionDecision
{
    /// <summary>Allow this single tool call.</summary>
    Approved,

    /// <summary>Deny this tool call.</summary>
    Denied,

    /// <summary>Allow this and all subsequent tool calls (switch to autopilot).</summary>
    ApproveAll,
}

/// <summary>
/// Represents a user input request from the agent (ask_user tool).
/// </summary>
public class AgentUserInputRequest
{
    /// <summary>The question the agent is asking.</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>Optional multiple-choice options.</summary>
    public List<string>? Choices { get; set; }

    /// <summary>Whether freeform text input is allowed (default: true).</summary>
    public bool AllowFreeform { get; set; } = true;
}

/// <summary>
/// The user's response to an agent input request.
/// </summary>
public class AgentUserInputResponse
{
    /// <summary>The user's answer text.</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>Whether the answer was freeform (not from choices).</summary>
    public bool WasFreeform { get; set; } = true;
}
