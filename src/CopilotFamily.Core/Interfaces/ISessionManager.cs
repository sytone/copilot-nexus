namespace CopilotFamily.Core.Interfaces;

using CopilotFamily.Core.Models;

/// <summary>
/// Manages the lifecycle of multiple Copilot SDK sessions.
/// </summary>
public interface ISessionManager : IAsyncDisposable
{
    IReadOnlyList<SessionInfo> Sessions { get; }

    /// <summary>Cached list of available models (populated after InitializeAsync).</summary>
    IReadOnlyList<ModelInfo> AvailableModels { get; }

    event EventHandler<SessionInfo>? SessionAdded;
    event EventHandler<SessionInfo>? SessionRemoved;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<SessionInfo> CreateSessionAsync(
        string name,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default);

    Task<SessionInfo> ResumeSessionAsync(
        string name,
        string sdkSessionId,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default);

    Task SendInputAsync(string sessionId, string input, CancellationToken cancellationToken = default);
    Task RemoveSessionAsync(string sessionId, bool deleteFromDisk = false, CancellationToken cancellationToken = default);
    ICopilotSessionWrapper? GetSession(string sessionId);

    /// <summary>
    /// Reconfigures a session by disconnecting and resuming with new settings.
    /// Used for changing model or working directory on a live session.
    /// </summary>
    Task<SessionInfo> ReconfigureSessionAsync(
        string sessionId,
        SessionConfiguration config,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default);
}
