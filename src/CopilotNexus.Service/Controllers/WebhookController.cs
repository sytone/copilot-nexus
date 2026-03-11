namespace CopilotNexus.Service.Controllers;

using CopilotNexus.Core.Contracts;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using CopilotNexus.Service.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

/// <summary>
/// Webhook endpoints for automation and script integration.
/// These endpoints are designed to be called by CI/CD pipelines,
/// scripts, or other automated systems.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly ISessionManager _sessionManager;
    private readonly IHubContext<SessionHub, ISessionHubClient> _hubContext;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        ISessionManager sessionManager,
        IHubContext<SessionHub, ISessionHubClient> hubContext,
        ILogger<WebhookController> logger)
    {
        _sessionManager = sessionManager;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Create a new session and send an initial message.
    /// Returns the session info immediately; output streams via SignalR.
    /// </summary>
    [HttpPost("sessions")]
    public async Task<ActionResult<SessionInfoDto>> CreateSessionWithMessage(
        [FromBody] WebhookCreateSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message is required for webhook session creation" });

        _logger.LogInformation("Webhook: creating session with message ({Length} chars), model={Model}",
            request.Message.Length, request.Model);

        var config = new SessionConfiguration
        {
            Model = request.Model,
            WorkingDirectory = request.WorkingDirectory,
            IsAutopilot = request.IsAutopilot,
        };

        var sessionInfo = await _sessionManager.CreateSessionAsync(
            $"Webhook {DateTime.Now:HH:mm:ss}",
            config,
            cancellationToken: cancellationToken);
        var dto = SessionInfoDto.FromSessionInfo(sessionInfo);

        // Notify all SignalR clients
        await _hubContext.Clients.All.SessionAdded(dto);

        // Wire output forwarding
        WireSessionOutputForwarding(sessionInfo.Id);

        // Send the initial message (non-blocking — runs in background)
        _ = Task.Run(async () =>
        {
            try
            {
                await _sessionManager.SendInputAsync(sessionInfo.Id, request.Message);

                if (!string.IsNullOrWhiteSpace(request.CallbackUrl))
                {
                    await PostCallbackAsync(request.CallbackUrl, sessionInfo.Id, "completed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook: failed to process message for session {SessionId}", sessionInfo.Id);
                if (!string.IsNullOrWhiteSpace(request.CallbackUrl))
                {
                    await PostCallbackAsync(request.CallbackUrl, sessionInfo.Id, "failed", ex.Message);
                }
            }
        }, cancellationToken);

        return CreatedAtAction(
            nameof(SessionsController.GetSession),
            "Sessions",
            new { id = sessionInfo.Id },
            dto);
    }

    /// <summary>
    /// Send a message to an existing session.
    /// Returns 202 Accepted; output streams via SignalR.
    /// </summary>
    [HttpPost("sessions/{id}/message")]
    public async Task<IActionResult> SendMessage(
        string id,
        [FromBody] WebhookMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message is required" });

        var info = _sessionManager.Sessions.FirstOrDefault(s => s.Id == id);
        if (info == null)
            return NotFound(new { error = $"Session '{id}' not found" });

        _logger.LogInformation("Webhook: sending message to session {SessionId} ({Length} chars)",
            id, request.Message.Length);

        // Non-blocking — runs in background
        _ = Task.Run(async () =>
        {
            try
            {
                await _sessionManager.SendInputAsync(id, request.Message);
                if (!string.IsNullOrWhiteSpace(request.CallbackUrl))
                {
                    await PostCallbackAsync(request.CallbackUrl, id, "completed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook: failed to send message to session {SessionId}", id);
                if (!string.IsNullOrWhiteSpace(request.CallbackUrl))
                {
                    await PostCallbackAsync(request.CallbackUrl, id, "failed", ex.Message);
                }
            }
        }, cancellationToken);

        return Accepted(new { sessionId = id, status = "processing" });
    }

    /// <summary>Abort the current request in a session.</summary>
    [HttpPost("sessions/{id}/abort")]
    public async Task<IActionResult> AbortSession(string id)
    {
        var session = _sessionManager.GetSession(id);
        if (session == null)
            return NotFound(new { error = $"Session '{id}' not found" });

        _logger.LogInformation("Webhook: aborting session {SessionId}", id);
        await session.AbortAsync();

        return Ok(new { sessionId = id, status = "aborted" });
    }

    private void WireSessionOutputForwarding(string sessionId)
    {
        var session = _sessionManager.GetSession(sessionId);
        if (session == null) return;

        session.OutputReceived += (_, e) =>
        {
            var dto = new SessionOutputDto(
                sessionId,
                e.Kind.ToString(),
                e.Role.ToString(),
                e.Content);

            _ = _hubContext.Clients.Group(sessionId).SessionOutput(sessionId, dto);
        };
    }

    private async Task PostCallbackAsync(string callbackUrl, string sessionId, string status, string? error = null)
    {
        try
        {
            using var httpClient = new HttpClient();
            var payload = new { sessionId, status, error, timestamp = DateTime.UtcNow };
            await httpClient.PostAsJsonAsync(callbackUrl, payload);
            _logger.LogDebug("Webhook callback sent to {Url}: {Status}", callbackUrl, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send webhook callback to {Url}", callbackUrl);
        }
    }
}
