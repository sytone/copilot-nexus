namespace CopilotFamily.Nexus.Hubs;

using CopilotFamily.Core.Contracts;
using CopilotFamily.Core.Events;
using CopilotFamily.Core.Interfaces;
using CopilotFamily.Core.Models;
using Microsoft.AspNetCore.SignalR;

/// <summary>
/// SignalR hub for real-time session interaction.
/// Clients join session groups to receive streaming output.
/// </summary>
public class SessionHub : Hub<ISessionHubClient>
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionHub> _logger;

    public SessionHub(ISessionManager sessionManager, ILogger<SessionHub> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);

        // Send available models to the newly connected client
        var models = _sessionManager.AvailableModels
            .Select(ModelInfoDto.FromModelInfo)
            .ToList();
        await Clients.Caller.ModelsLoaded(models);

        // Send all existing sessions
        foreach (var session in _sessionManager.Sessions)
        {
            await Clients.Caller.SessionAdded(SessionInfoDto.FromSessionInfo(session));
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId} (reason: {Reason})",
            Context.ConnectionId, exception?.Message ?? "clean");
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Subscribe to output from a specific session.</summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogDebug("Client {ConnectionId} joined session {SessionId}", Context.ConnectionId, sessionId);

        var session = _sessionManager.GetSession(sessionId);
        if (session != null)
        {
            session.OutputReceived += (_, e) => OnSessionOutput(sessionId, e);
        }
    }

    /// <summary>Unsubscribe from a session's output.</summary>
    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogDebug("Client {ConnectionId} left session {SessionId}", Context.ConnectionId, sessionId);
    }

    /// <summary>Send text input to a session.</summary>
    public async Task SendInput(string sessionId, string input)
    {
        _logger.LogDebug("Client {ConnectionId} sending input to {SessionId} ({Length} chars)",
            Context.ConnectionId, sessionId, input.Length);

        try
        {
            await _sessionManager.SendInputAsync(sessionId, input);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send input to session {SessionId}", sessionId);
            throw new HubException($"Failed to send input: {ex.Message}");
        }
    }

    /// <summary>Abort the current request in a session.</summary>
    public async Task AbortSession(string sessionId)
    {
        _logger.LogInformation("Client {ConnectionId} aborting session {SessionId}",
            Context.ConnectionId, sessionId);

        var session = _sessionManager.GetSession(sessionId);
        if (session != null)
        {
            await session.AbortAsync();
        }
    }

    private void OnSessionOutput(string sessionId, SessionOutputEventArgs e)
    {
        var dto = new SessionOutputDto(
            sessionId,
            e.Kind.ToString(),
            e.Role.ToString(),
            e.Content);

        // Fire and forget — hub context handles thread safety
        _ = Clients.Group(sessionId).SessionOutput(sessionId, dto);
    }
}
