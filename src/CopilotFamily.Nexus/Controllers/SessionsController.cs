namespace CopilotFamily.Nexus.Controllers;

using CopilotFamily.Core.Contracts;
using CopilotFamily.Core.Interfaces;
using CopilotFamily.Core.Models;
using CopilotFamily.Nexus.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly ISessionManager _sessionManager;
    private readonly IHubContext<SessionHub, ISessionHubClient> _hubContext;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ISessionManager sessionManager,
        IHubContext<SessionHub, ISessionHubClient> hubContext,
        ILogger<SessionsController> logger)
    {
        _sessionManager = sessionManager;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>List all active sessions.</summary>
    [HttpGet]
    public ActionResult<List<SessionInfoDto>> ListSessions()
    {
        var sessions = _sessionManager.Sessions
            .Select(SessionInfoDto.FromSessionInfo)
            .ToList();
        return Ok(sessions);
    }

    /// <summary>Get a specific session.</summary>
    [HttpGet("{id}")]
    public ActionResult<SessionInfoDto> GetSession(string id)
    {
        var info = _sessionManager.Sessions.FirstOrDefault(s => s.Id == id);
        if (info == null)
            return NotFound(new { error = $"Session '{id}' not found" });

        return Ok(SessionInfoDto.FromSessionInfo(info));
    }

    /// <summary>Create a new session.</summary>
    [HttpPost]
    public async Task<ActionResult<SessionInfoDto>> CreateSession(
        [FromBody] CreateSessionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating session: model={Model}, workDir={WorkDir}",
            request?.Model, request?.WorkingDirectory);

        var config = request != null
            ? new SessionConfiguration
            {
                Model = request.Model,
                WorkingDirectory = request.WorkingDirectory,
                IsAutopilot = request.IsAutopilot,
            }
            : null;

        var sessionInfo = await _sessionManager.CreateSessionAsync(
            request?.Name ?? $"Session {DateTime.Now:HH:mm:ss}",
            config,
            cancellationToken: cancellationToken);
        var dto = SessionInfoDto.FromSessionInfo(sessionInfo);

        // Notify all connected clients
        await _hubContext.Clients.All.SessionAdded(dto);

        // Wire up output forwarding for this session
        WireSessionOutputForwarding(sessionInfo.Id);

        // Send initial message if provided
        if (!string.IsNullOrWhiteSpace(request?.InitialMessage))
        {
            await _sessionManager.SendInputAsync(sessionInfo.Id, request.InitialMessage);
        }

        return CreatedAtAction(nameof(GetSession), new { id = sessionInfo.Id }, dto);
    }

    /// <summary>Delete/close a session.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSession(string id)
    {
        var info = _sessionManager.Sessions.FirstOrDefault(s => s.Id == id);
        if (info == null)
            return NotFound(new { error = $"Session '{id}' not found" });

        _logger.LogInformation("Deleting session {SessionId}", id);
        await _sessionManager.RemoveSessionAsync(id, deleteFromDisk: true);

        await _hubContext.Clients.All.SessionRemoved(id);
        return NoContent();
    }

    /// <summary>Reconfigure a session (change model, working directory, autopilot).</summary>
    [HttpPut("{id}/configure")]
    public async Task<ActionResult<SessionInfoDto>> ConfigureSession(
        string id,
        [FromBody] ConfigureSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var info = _sessionManager.Sessions.FirstOrDefault(s => s.Id == id);
        if (info == null)
            return NotFound(new { error = $"Session '{id}' not found" });

        _logger.LogInformation("Reconfiguring session {SessionId}: model={Model}, workDir={WorkDir}",
            id, request.Model, request.WorkingDirectory);

        var config = new SessionConfiguration
        {
            Model = request.Model ?? info.Model,
            WorkingDirectory = request.WorkingDirectory ?? info.WorkingDirectory,
            IsAutopilot = request.IsAutopilot ?? info.IsAutopilot,
        };

        var newInfo = await _sessionManager.ReconfigureSessionAsync(
            id, config, cancellationToken: cancellationToken);

        var dto = SessionInfoDto.FromSessionInfo(newInfo);
        await _hubContext.Clients.All.SessionReconfigured(dto);

        // Re-wire output forwarding with new session wrapper
        WireSessionOutputForwarding(id);

        return Ok(dto);
    }

    /// <summary>Send text input to a session.</summary>
    [HttpPost("{id}/input")]
    public async Task<IActionResult> SendInput(
        string id,
        [FromBody] SendInputRequest request)
    {
        var info = _sessionManager.Sessions.FirstOrDefault(s => s.Id == id);
        if (info == null)
            return NotFound(new { error = $"Session '{id}' not found" });

        if (string.IsNullOrWhiteSpace(request.Input))
            return BadRequest(new { error = "Input cannot be empty" });

        _logger.LogDebug("Sending input to session {SessionId} ({Length} chars)", id, request.Input.Length);
        await _sessionManager.SendInputAsync(id, request.Input);

        return Accepted();
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
}
