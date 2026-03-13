namespace CopilotNexus.Service.Hubs;

using CopilotNexus.Core.Contracts;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.AspNetCore.SignalR;

/// <summary>
/// SignalR hub for real-time session interaction.
/// Clients join session groups to receive streaming output.
/// </summary>
public class SessionHub : Hub<ISessionHubClient>
{
    private readonly ISessionManager _sessionManager;
    private readonly IHubContext<SessionHub, ISessionHubClient> _hubContext;
    private readonly ILogger<SessionHub> _logger;

    public SessionHub(
        ISessionManager sessionManager,
        IHubContext<SessionHub, ISessionHubClient> hubContext,
        ILogger<SessionHub> logger)
    {
        _sessionManager = sessionManager;
        _hubContext = hubContext;
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
    }

    /// <summary>Unsubscribe from a session's output.</summary>
    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogDebug("Client {ConnectionId} left session {SessionId}", Context.ConnectionId, sessionId);
    }

    /// <summary>Send text input to a session.</summary>
    public Task SendInput(string sessionId, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new HubException("Input cannot be empty");

        _logger.LogDebug("Client {ConnectionId} sending input to {SessionId} ({Length} chars)",
            Context.ConnectionId, sessionId, input.Length);

        var session = _sessionManager.GetSession(sessionId);
        if (session == null)
            throw new HubException($"Session '{sessionId}' not found");

        _ = Task.Run(() => DispatchSendInputAsync(sessionId, input, Context.ConnectionId));
        return Task.CompletedTask;
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

    private async Task DispatchSendInputAsync(string sessionId, string input, string connectionId)
    {
        try
        {
            await _sessionManager.SendInputAsync(sessionId, input);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(
                ex,
                "Send canceled for session {SessionId} requested by {ConnectionId}",
                sessionId,
                connectionId);
            await NotifySendFailureAsync(sessionId, "Request canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Background send failed for session {SessionId} requested by {ConnectionId}",
                sessionId,
                connectionId);
            await NotifySendFailureAsync(sessionId, $"Error: {ex.Message}");
        }
    }

    private async Task NotifySendFailureAsync(string sessionId, string message)
    {
        var errorOutput = new SessionOutputDto(
            sessionId,
            OutputKind.Activity.ToString(),
            MessageRole.System.ToString(),
            message);
        var idleOutput = new SessionOutputDto(
            sessionId,
            OutputKind.Idle.ToString(),
            MessageRole.System.ToString(),
            string.Empty);

        await _hubContext.Clients.Group(sessionId).SessionOutput(sessionId, errorOutput);
        await _hubContext.Clients.Group(sessionId).SessionOutput(sessionId, idleOutput);
    }
}
