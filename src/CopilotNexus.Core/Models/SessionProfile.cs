namespace CopilotNexus.Core.Models;

/// <summary>
/// Reusable defaults for creating/resuming sessions.
/// Profiles are owned by Nexus so multiple clients can reuse them.
/// </summary>
public class SessionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Default";
    public string? Description { get; set; }
    public string? Model { get; set; }
    public bool IsAutopilot { get; set; } = true;
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Optional path to a custom agent markdown file.
    /// This file is loaded into CustomAgents for session creation/resume.
    /// </summary>
    public string? AgentFilePath { get; set; }

    /// <summary>
    /// Enables discovery of MCP config files in well-known locations.
    /// </summary>
    public bool IncludeWellKnownMcpConfigs { get; set; } = true;

    /// <summary>
    /// Semicolon/newline-delimited file paths to additional MCP config files.
    /// </summary>
    public string AdditionalMcpConfigPaths { get; set; } = string.Empty;

    /// <summary>
    /// Comma/semicolon/newline-delimited server names to enable.
    /// Empty means all configured servers.
    /// </summary>
    public string EnabledMcpServers { get; set; } = string.Empty;

    /// <summary>
    /// Semicolon/newline-delimited skill directories to include.
    /// </summary>
    public string AdditionalSkillDirectories { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Persisted wrapper for profile storage.
/// </summary>
public class SessionProfilesState
{
    public int Version { get; set; } = 1;
    public List<SessionProfile> Profiles { get; set; } = new();
}
