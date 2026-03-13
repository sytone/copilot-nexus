namespace CopilotNexus.Service.Controllers;

using CopilotNexus.Core.Contracts;
using CopilotNexus.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class ModelsController : ControllerBase
{
    private readonly IAgentClientService _runtimeClient;
    public ModelsController(IAgentClientService runtimeClient) => _runtimeClient = runtimeClient;

    /// <summary>List all available models.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ModelInfoDto>>> ListModels(CancellationToken cancellationToken = default)
    {
        var models = await _runtimeClient.ListModelsAsync(cancellationToken);

        var output = models
            .Select(ModelInfoDto.FromModelInfo)
            .ToList();
        return Ok(output);
    }
}
