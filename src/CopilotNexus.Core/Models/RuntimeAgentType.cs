namespace CopilotNexus.Core.Models;

/// <summary>
/// Supported runtime backends for Nexus session execution.
/// </summary>
public enum RuntimeAgentType
{
    Pi,
    CopilotSdk,
}

public static class RuntimeAgentTypeExtensions
{
    private const string PiValue = "pi";
    private const string CopilotSdkValue = "copilot-sdk";

    public static string ToConfigValue(this RuntimeAgentType runtimeAgent) => runtimeAgent switch
    {
        RuntimeAgentType.Pi => PiValue,
        RuntimeAgentType.CopilotSdk => CopilotSdkValue,
        _ => CopilotSdkValue,
    };

    public static bool TryParse(string? rawValue, out RuntimeAgentType runtimeAgent)
    {
        var normalized = rawValue?.Trim();
        if (string.Equals(normalized, PiValue, StringComparison.OrdinalIgnoreCase))
        {
            runtimeAgent = RuntimeAgentType.Pi;
            return true;
        }

        if (string.Equals(normalized, CopilotSdkValue, StringComparison.OrdinalIgnoreCase))
        {
            runtimeAgent = RuntimeAgentType.CopilotSdk;
            return true;
        }

        runtimeAgent = RuntimeAgentType.CopilotSdk;
        return false;
    }

    public static string SupportedValuesHint() => $"{PiValue}|{CopilotSdkValue}";
}
