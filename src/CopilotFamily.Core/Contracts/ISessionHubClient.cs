namespace CopilotFamily.Core.Contracts;

/// <summary>
/// SignalR client interface — methods the server can invoke on connected clients.
/// Used by both the Nexus hub and the Avalonia SignalR client.
/// </summary>
public interface ISessionHubClient
{
    /// <summary>Streaming output from a session (deltas, messages, idle).</summary>
    Task SessionOutput(string sessionId, SessionOutputDto output);

    /// <summary>Session state changed (Running, Stopped, etc.).</summary>
    Task SessionStateChanged(string sessionId, string state);

    /// <summary>A new session was created (by this or another client).</summary>
    Task SessionAdded(SessionInfoDto session);

    /// <summary>A session was removed.</summary>
    Task SessionRemoved(string sessionId);

    /// <summary>Available models have been loaded.</summary>
    Task ModelsLoaded(List<ModelInfoDto> models);

    /// <summary>Session was reconfigured (model change, etc.).</summary>
    Task SessionReconfigured(SessionInfoDto session);
}
