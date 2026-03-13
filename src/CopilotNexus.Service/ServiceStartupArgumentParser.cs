namespace CopilotNexus.Service;

using CopilotNexus.Core.Models;

public static class ServiceStartupArgumentParser
{
    public static ServiceStartupArgs Parse(string[] inputArgs)
    {
        var forwarded = new List<string>();
        RuntimeAgentType? agentOverride = null;

        for (var index = 0; index < inputArgs.Length; index++)
        {
            var arg = inputArgs[index];
            if (string.Equals(arg, "--agent", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= inputArgs.Length)
                {
                    return new ServiceStartupArgs([], null, $"Missing value for --agent. Supported values: {RuntimeAgentTypeExtensions.SupportedValuesHint()}");
                }

                var value = inputArgs[++index];
                if (!RuntimeAgentTypeExtensions.TryParse(value, out var parsed))
                {
                    return new ServiceStartupArgs([], null, $"Invalid --agent '{value}'. Supported values: {RuntimeAgentTypeExtensions.SupportedValuesHint()}");
                }

                agentOverride = parsed;
                continue;
            }

            const string prefixedAgent = "--agent=";
            if (arg.StartsWith(prefixedAgent, StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[prefixedAgent.Length..];
                if (!RuntimeAgentTypeExtensions.TryParse(value, out var parsed))
                {
                    return new ServiceStartupArgs([], null, $"Invalid --agent '{value}'. Supported values: {RuntimeAgentTypeExtensions.SupportedValuesHint()}");
                }

                agentOverride = parsed;
                continue;
            }

            forwarded.Add(arg);
        }

        return new ServiceStartupArgs(forwarded, agentOverride, null);
    }
}

public sealed record ServiceStartupArgs(
    IReadOnlyList<string> ForwardedArgs,
    RuntimeAgentType? AgentOverride,
    string? Error);
