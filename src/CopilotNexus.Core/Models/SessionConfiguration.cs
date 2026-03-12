namespace CopilotNexus.Core.Models;

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

    /// <summary>Optional profile identifier used to derive this session configuration.</summary>
    public string? ProfileId { get; set; }

    /// <summary>Optional path to a custom agent markdown file.</summary>
    public string? AgentFilePath { get; set; }

    /// <summary>Enable MCP config discovery from well-known config locations.</summary>
    public bool IncludeWellKnownMcpConfigs { get; set; } = true;

    /// <summary>Additional MCP config files to merge for this session.</summary>
    public List<string> AdditionalMcpConfigPaths { get; set; } = new();

    /// <summary>Optional list of MCP server names to enable from merged configuration.</summary>
    public List<string> EnabledMcpServers { get; set; } = new();

    /// <summary>Additional skill directories to load for the session.</summary>
    public List<string> SkillDirectories { get; set; } = new();
}
