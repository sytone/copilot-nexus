namespace CopilotNexus.Service.Controllers;

using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/session-profiles")]
public class SessionProfilesController : ControllerBase
{
    private readonly ISessionProfileService _profiles;

    public SessionProfilesController(ISessionProfileService profiles)
    {
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SessionProfile>>> List(CancellationToken cancellationToken = default)
    {
        var items = await _profiles.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SessionProfile>> Get(string id, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(id, cancellationToken);
        if (profile == null)
            return NotFound(new { error = $"Profile '{id}' not found" });

        return Ok(profile);
    }

    [HttpPost]
    public async Task<ActionResult<SessionProfile>> Create([FromBody] SessionProfile profile, CancellationToken cancellationToken = default)
    {
        profile.Id = string.Empty;
        var saved = await _profiles.SaveAsync(profile, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, saved);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<SessionProfile>> Update(
        string id,
        [FromBody] SessionProfile profile,
        CancellationToken cancellationToken = default)
    {
        profile.Id = id;
        var saved = await _profiles.SaveAsync(profile, cancellationToken);
        return Ok(saved);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken = default)
    {
        await _profiles.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
