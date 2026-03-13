namespace CopilotNexus.Service.Controllers;

using CopilotNexus.Core.Contracts;
using CopilotNexus.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class ModelsController : ControllerBase
{
    private readonly ISessionManager _sessionManager;
    public ModelsController(ISessionManager sessionManager) => _sessionManager = sessionManager;

    /// <summary>List all available models.</summary>
    [HttpGet]
    public ActionResult<List<ModelInfoDto>> ListModels()
    {
        var models = _sessionManager.AvailableModels;

        var output = models
            .Select(ModelInfoDto.FromModelInfo)
            .ToList();
        return Ok(output);
    }
}
