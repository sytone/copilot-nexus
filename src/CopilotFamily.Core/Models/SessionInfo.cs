namespace CopilotFamily.Core.Models;

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
    public static SessionInfo FromRemote(string id, string name, string? model, string sdkSessionId, bool isAutopilot = true, string? workingDirectory = null)
    {
        var info = new SessionInfo(name, model, sdkSessionId)
        {
            Id = id,
            IsAutopilot = isAutopilot,
            WorkingDirectory = workingDirectory,
            State = SessionState.Running
        };
        return info;
    }
}
