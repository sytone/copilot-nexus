namespace CopilotNexus.Core.Models;

/// <summary>
/// Persisted runtime-agent selection for Nexus service startup.
/// </summary>
public sealed class RuntimeAgentConfig
{
    public string Agent { get; set; } = RuntimeAgentType.CopilotSdk.ToConfigValue();
}
