namespace CopilotNexus.Service.Controllers;

using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/app-state")]
public class AppStateController : ControllerBase
{
    private readonly IStatePersistenceService _statePersistence;
    private readonly ILogger<AppStateController> _logger;

    public AppStateController(IStatePersistenceService statePersistence, ILogger<AppStateController> logger)
    {
        _statePersistence = statePersistence;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<AppState>> Get(CancellationToken cancellationToken = default)
    {
        var state = await _statePersistence.LoadAsync(cancellationToken);
        if (state == null)
            return NoContent();

        return Ok(state);
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] AppState state, CancellationToken cancellationToken = default)
    {
        if (state == null)
            return BadRequest(new { error = "State payload is required." });

        await _statePersistence.SaveAsync(state, cancellationToken);
        _logger.LogInformation("App state saved via API ({TabCount} tabs)", state.Tabs.Count);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken = default)
    {
        await _statePersistence.ClearAsync(cancellationToken);
        _logger.LogInformation("App state cleared via API");
        return NoContent();
    }
}
