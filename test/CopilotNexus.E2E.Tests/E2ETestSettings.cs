namespace CopilotNexus.E2E.Tests;

internal static class E2ETestSettings
{
    public static bool Enabled => IsTruthy(Environment.GetEnvironmentVariable("NEXUS_E2E_ENABLED"));
    public static bool ManageService => IsTruthy(Environment.GetEnvironmentVariable("NEXUS_E2E_MANAGE_SERVICE"));

    public static string BaseUrl =>
        (Environment.GetEnvironmentVariable("NEXUS_E2E_BASE_URL") ?? "http://localhost:5280").TrimEnd('/');

    public static string ManagedServiceUrl =>
        (Environment.GetEnvironmentVariable("NEXUS_E2E_MANAGED_URL") ?? "http://localhost:5290").TrimEnd('/');

    public static string RepoRoot { get; } = ResolveRepoRoot();
    public static string CliProjectPath => Path.Combine(RepoRoot, "src", "CopilotNexus.Cli", "CopilotNexus.Cli.csproj");

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static string ResolveRepoRoot()
    {
        var candidate = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            if (File.Exists(Path.Combine(candidate, "CopilotNexus.slnx")))
                return candidate;

            candidate = Directory.GetParent(candidate)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
