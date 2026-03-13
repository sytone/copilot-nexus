namespace CopilotNexus.Core.Interfaces;

using CopilotNexus.Core.Models;

/// <summary>
/// Generic agent runtime client abstraction.
/// Implementations can target Copilot SDK, Pi RPC, or other runtimes.
/// </summary>
public interface IAgentClientService : IAsyncDisposable
{
    bool IsConnected { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists available models for this adapter.</summary>
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
