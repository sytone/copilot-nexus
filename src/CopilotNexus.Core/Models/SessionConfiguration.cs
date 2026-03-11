namespace CopilotFamily.Core.Models;

/// <summary>
/// Configuration for creating or resuming a Copilot session.
/// Bundles model, working directory, and autopilot settings.
/// </summary>
public class SessionConfiguration
{
    /// <summary>LLM model to use (e.g., "gpt-5", "claude-sonnet-4.5").</summary>
    public string? Model { get; set; }

    /// <summary>Working directory for file operations (the project/repo path).</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Whether the session runs in autopilot mode (auto-approve all tool calls).</summary>
    public bool IsAutopilot { get; set; } = true;
}
