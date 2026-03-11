namespace CopilotNexus.Core.Models;

/// <summary>
/// Represents an available LLM model that can be selected for a session.
/// </summary>
public class ModelInfo
{
    /// <summary>Model identifier used in SessionConfig.Model (e.g., "gpt-5", "claude-sonnet-4.5").</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Capabilities supported by this model (e.g., reasoning, streaming).</summary>
    public List<string> Capabilities { get; set; } = new();

    public override string ToString() => string.IsNullOrEmpty(Name) ? ModelId : Name;
}
