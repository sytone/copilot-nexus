namespace CopilotNexus.E2E.Tests;

using Xunit;

internal sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!E2ETestSettings.Enabled)
        {
            Skip = "Set NEXUS_E2E_ENABLED=1 to run E2E tests.";
        }
    }
}

internal sealed class ManagedServiceE2EFactAttribute : FactAttribute
{
    public ManagedServiceE2EFactAttribute()
    {
        if (!E2ETestSettings.Enabled)
        {
            Skip = "Set NEXUS_E2E_ENABLED=1 to run managed-service E2E tests.";
            return;
        }

        if (!E2ETestSettings.ManageService)
        {
            Skip = "Set NEXUS_E2E_MANAGE_SERVICE=1 to run managed-service start/stop/update tests.";
        }
    }
}
