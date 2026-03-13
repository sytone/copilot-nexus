namespace CopilotNexus.Core.Interfaces;

using CopilotNexus.Core.Models;

/// <summary>
/// Loads and persists the selected runtime backend for Nexus service startup.
/// </summary>
public interface IRuntimeAgentConfigService
{
    Task<RuntimeAgentType> GetAsync(CancellationToken cancellationToken = default);
    Task SetAsync(RuntimeAgentType runtimeAgent, CancellationToken cancellationToken = default);
}
