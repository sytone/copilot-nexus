namespace CopilotNexus.Core.Models;

public class SessionInfo
{
    public string Id { get; private set; }
    public string Name { get; set; }
    public string? Model { get; set; }

    /// <summary>
    /// The SDK-managed session ID used for persistence and resumption.
    /// </summary>
    public string SdkSessionId { get; }

    /// <summary>
    /// Working directory for file operations in this session.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Whether this session is in autopilot mode (auto-approve all tool calls).
    /// </summary>
    public bool IsAutopilot { get; set; } = true;

    /// <summary>Optional profile ID used for this session.</summary>
    public string? ProfileId { get; set; }

    /// <summary>Optional custom agent file path applied to this session.</summary>
    public string? AgentFilePath { get; set; }

    /// <summary>Whether well-known MCP config locations are included.</summary>
    public bool IncludeWellKnownMcpConfigs { get; set; } = true;

    /// <summary>Additional MCP config file paths merged into session MCP servers.</summary>
    public List<string> AdditionalMcpConfigPaths { get; set; } = new();

    /// <summary>Optional list of enabled MCP server names for this session.</summary>
    public List<string> EnabledMcpServers { get; set; } = new();

    /// <summary>Additional skill directories loaded for this session.</summary>
    public List<string> SkillDirectories { get; set; } = new();

    public SessionState State { get; set; }
    public DateTime CreatedAt { get; }

    public SessionInfo(string name, string? model = null, string? sdkSessionId = null)
    {
        Id = Guid.NewGuid().ToString("N")[..8];
        Name = name;
        Model = model;
        SdkSessionId = sdkSessionId ?? Id;
        State = SessionState.NotStarted;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Factory for recreating a SessionInfo from persisted/remote data (e.g., Nexus DTO).
    /// </summary>
    public static SessionInfo FromRemote(
        string id,
        string name,
        string? model,
        string sdkSessionId,
        bool isAutopilot = true,
        string? workingDirectory = null,
        string? profileId = null,
        string? agentFilePath = null,
        bool includeWellKnownMcpConfigs = true,
        IEnumerable<string>? additionalMcpConfigPaths = null,
        IEnumerable<string>? enabledMcpServers = null,
        IEnumerable<string>? skillDirectories = null)
    {
        var info = new SessionInfo(name, model, sdkSessionId)
        {
            Id = id,
            IsAutopilot = isAutopilot,
            WorkingDirectory = workingDirectory,
            ProfileId = profileId,
            AgentFilePath = agentFilePath,
            IncludeWellKnownMcpConfigs = includeWellKnownMcpConfigs,
            AdditionalMcpConfigPaths = additionalMcpConfigPaths?.ToList() ?? new List<string>(),
            EnabledMcpServers = enabledMcpServers?.ToList() ?? new List<string>(),
            SkillDirectories = skillDirectories?.ToList() ?? new List<string>(),
            State = SessionState.Running,
        };
        return info;
    }
}
