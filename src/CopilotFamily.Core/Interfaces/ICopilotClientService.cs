namespace CopilotFamily.Core.Interfaces;

using CopilotFamily.Core.Models;

/// <summary>
/// Manages the single CopilotClient connection to the Copilot CLI (via JSON-RPC).
/// One instance per application — shared across all session tabs.
/// </summary>
public interface ICopilotClientService : IAsyncDisposable
{
    bool IsConnected { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists all models available to the authenticated user.</summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a new session with the given configuration.</summary>
    Task<ICopilotSessionWrapper> CreateSessionAsync(
        string? sessionId = null,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default);

    /// <summary>Resumes an existing session with optional reconfiguration.</summary>
    Task<ICopilotSessionWrapper> ResumeSessionAsync(
        string sessionId,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
