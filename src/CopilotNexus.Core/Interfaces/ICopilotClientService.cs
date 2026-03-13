namespace CopilotNexus.Core.Interfaces;

using CopilotNexus.Core.Models;

/// <summary>
/// Manages the single CopilotClient connection to the Copilot CLI (via JSON-RPC).
/// One instance per application — shared across all session tabs.
/// </summary>
public interface ICopilotClientService : IAgentClientService
{
    // Backward-compatible alias for the Copilot-specific adapter contract.
}
