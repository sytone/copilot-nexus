namespace CopilotNexus.Service.Controllers;

using CopilotNexus.Core.Contracts;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using CopilotNexus.Service.Hubs;
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

    /// <summary>Get persisted message history for a session.</summary>
    [HttpGet("{id}/history")]
    public async Task<ActionResult<List<SessionOutputDto>>> GetSessionHistory(
        string id,
        CancellationToken cancellationToken = default)
    {
        var session = _sessionManager.GetSession(id);
        if (session == null)
            return NotFound(new { error = $"Session '{id}' not found" });

        var history = await session.GetHistoryAsync(cancellationToken);
        var output = history
            .Where(item => item.Kind == OutputKind.Message)
            .Where(item => !string.IsNullOrWhiteSpace(item.Content))
            .Select(item => new SessionOutputDto(
                id,
                item.Kind.ToString(),
                item.Role.ToString(),
                item.Content))
            .ToList();

        return Ok(output);
    }

    /// <summary>Create a new session.</summary>
    [HttpPost]
    public async Task<ActionResult<SessionInfoDto>> CreateSession(
        [FromBody] CreateSessionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating session: model={Model}, workDir={WorkDir}, sdkSessionId={SdkSessionId}",
            request?.Model, request?.WorkingDirectory, request?.SdkSessionId);

        var config = request != null
            ? new SessionConfiguration
            {
                Model = request.Model,
                WorkingDirectory = request.WorkingDirectory,
                IsAutopilot = request.IsAutopilot,
                ProfileId = request.ProfileId,
                AgentFilePath = request.AgentFilePath,
                IncludeWellKnownMcpConfigs = request.IncludeWellKnownMcpConfigs,
                AdditionalMcpConfigPaths = request.AdditionalMcpConfigPaths ?? [],
                EnabledMcpServers = request.EnabledMcpServers ?? [],
                SkillDirectories = request.SkillDirectories ?? [],
            }
            : null;

        var sessionName = request?.Name ?? $"Session {DateTime.Now:HH:mm:ss}";
        SessionInfo sessionInfo;

        if (!string.IsNullOrWhiteSpace(request?.SdkSessionId))
        {
            sessionInfo = await _sessionManager.ResumeSessionAsync(
                sessionName,
                request.SdkSessionId,
                config,
                cancellationToken: cancellationToken);
        }
        else
        {
            sessionInfo = await _sessionManager.CreateSessionAsync(
                sessionName,
                config,
                cancellationToken: cancellationToken);
        }
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
            ProfileId = request.ProfileId ?? info.ProfileId,
            AgentFilePath = request.AgentFilePath ?? info.AgentFilePath,
            IncludeWellKnownMcpConfigs = request.IncludeWellKnownMcpConfigs ?? info.IncludeWellKnownMcpConfigs,
            AdditionalMcpConfigPaths = request.AdditionalMcpConfigPaths ?? info.AdditionalMcpConfigPaths,
            EnabledMcpServers = request.EnabledMcpServers ?? info.EnabledMcpServers,
            SkillDirectories = request.SkillDirectories ?? info.SkillDirectories,
        };

        var newInfo = await _sessionManager.ReconfigureSessionAsync(
            id, config, cancellationToken: cancellationToken);

        var dto = SessionInfoDto.FromSessionInfo(newInfo);
        await _hubContext.Clients.All.SessionReconfigured(dto);

        // Re-wire output forwarding with new session wrapper
        WireSessionOutputForwarding(newInfo.Id);

        return Ok(dto);
    }

    /// <summary>Rename an existing session.</summary>
    [HttpPut("{id}/name")]
    public async Task<ActionResult<SessionInfoDto>> RenameSession(
        string id,
        [FromBody] RenameSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = _sessionManager.Sessions.FirstOrDefault(s => s.Id == id);
        if (existing == null)
            return NotFound(new { error = $"Session '{id}' not found" });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name cannot be empty." });

        var info = await _sessionManager.RenameSessionAsync(id, request.Name, cancellationToken);
        var dto = SessionInfoDto.FromSessionInfo(info);
        await _hubContext.Clients.All.SessionReconfigured(dto);
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
