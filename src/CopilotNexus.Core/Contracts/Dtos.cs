namespace CopilotNexus.Core.Contracts;

using CopilotNexus.Core.Models;

/// <summary>DTO for session output streamed via SignalR.</summary>
public record SessionOutputDto(
    string SessionId,
    string Kind,
    string Role,
    string Content);

/// <summary>DTO for session info sent via SignalR and REST.</summary>
public record SessionInfoDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Model { get; init; }
    public string SdkSessionId { get; init; } = string.Empty;
    public string? WorkingDirectory { get; init; }
    public bool IsAutopilot { get; init; } = true;
    public string? ProfileId { get; init; }
    public string? AgentFilePath { get; init; }
    public bool IncludeWellKnownMcpConfigs { get; init; } = true;
    public List<string> AdditionalMcpConfigPaths { get; init; } = new();
    public List<string> EnabledMcpServers { get; init; } = new();
    public List<string> SkillDirectories { get; init; } = new();
    public string State { get; init; } = "Running";
    public DateTime CreatedAt { get; init; }

    public static SessionInfoDto FromSessionInfo(SessionInfo info) => new()
    {
        Id = info.Id,
        Name = info.Name,
        Model = info.Model,
        SdkSessionId = info.SdkSessionId,
        WorkingDirectory = info.WorkingDirectory,
        IsAutopilot = info.IsAutopilot,
        ProfileId = info.ProfileId,
        AgentFilePath = info.AgentFilePath,
        IncludeWellKnownMcpConfigs = info.IncludeWellKnownMcpConfigs,
        AdditionalMcpConfigPaths = new List<string>(info.AdditionalMcpConfigPaths ?? []),
        EnabledMcpServers = new List<string>(info.EnabledMcpServers ?? []),
        SkillDirectories = new List<string>(info.SkillDirectories ?? []),
        State = info.State.ToString(),
        CreatedAt = info.CreatedAt,
    };
}

/// <summary>DTO for model info sent via SignalR and REST.</summary>
public record ModelInfoDto
{
    public string ModelId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<string> Capabilities { get; init; } = new();

    public static ModelInfoDto FromModelInfo(ModelInfo info) => new()
    {
        ModelId = info.ModelId,
        Name = info.Name,
        Capabilities = info.Capabilities,
    };
}

/// <summary>Request body for creating a session.</summary>
public record CreateSessionRequest
{
    public string? Name { get; init; }
    /// <summary>Optional SDK session ID to resume an existing persisted session.</summary>
    public string? SdkSessionId { get; init; }
    public string? Model { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool IsAutopilot { get; init; } = true;
    public string? ProfileId { get; init; }
    public string? AgentFilePath { get; init; }
    public bool IncludeWellKnownMcpConfigs { get; init; } = true;
    public List<string> AdditionalMcpConfigPaths { get; init; } = new();
    public List<string> EnabledMcpServers { get; init; } = new();
    public List<string> SkillDirectories { get; init; } = new();
    /// <summary>Optional initial message to send after creation.</summary>
    public string? InitialMessage { get; init; }
}

/// <summary>Request body for reconfiguring a session.</summary>
public record ConfigureSessionRequest
{
    public string? Name { get; init; }
    public string? Model { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool? IsAutopilot { get; init; }
    public string? ProfileId { get; init; }
    public string? AgentFilePath { get; init; }
    public bool? IncludeWellKnownMcpConfigs { get; init; }
    public List<string>? AdditionalMcpConfigPaths { get; init; }
    public List<string>? EnabledMcpServers { get; init; }
    public List<string>? SkillDirectories { get; init; }
}

/// <summary>Request body for renaming a session.</summary>
public record RenameSessionRequest
{
    public string Name { get; init; } = string.Empty;
}

/// <summary>Request body for sending input to a session.</summary>
public record SendInputRequest
{
    public string Input { get; init; } = string.Empty;
}

/// <summary>Webhook request for creating a session with an initial message.</summary>
public record WebhookCreateSessionRequest
{
    public string? Model { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool IsAutopilot { get; init; } = true;
    public string Message { get; init; } = string.Empty;
    /// <summary>Optional callback URL to POST results to when complete.</summary>
    public string? CallbackUrl { get; init; }
}

/// <summary>Webhook request for sending a message to an existing session.</summary>
public record WebhookMessageRequest
{
    public string Message { get; init; } = string.Empty;
    /// <summary>Optional callback URL to POST results to when complete.</summary>
    public string? CallbackUrl { get; init; }
}
