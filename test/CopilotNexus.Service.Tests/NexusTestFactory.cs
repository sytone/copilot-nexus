namespace CopilotNexus.Service.Tests;

using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// WebApplicationFactory that swaps the real SDK for mock services.
/// Sets NEXUS_TEST_MODE so Program.Main takes the web host path
/// instead of System.CommandLine routing.
/// </summary>
public class NexusTestFactory : WebApplicationFactory<Program>
{
    public NexusTestFactory()
    {
        Environment.SetEnvironmentVariable("NEXUS_TEST_MODE", "1");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real SDK client registration
            var clientDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICopilotClientService));
            if (clientDescriptor != null) services.Remove(clientDescriptor);

            // Remove real session manager
            var managerDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISessionManager));
            if (managerDescriptor != null) services.Remove(managerDescriptor);

            // Register mock client
            services.AddSingleton<ICopilotClientService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<MockCopilotClientService>>();
                return new MockCopilotClientService(logger);
            });

            // Register session manager with mock client
            services.AddSingleton<ISessionManager>(sp =>
            {
                var client = sp.GetRequiredService<ICopilotClientService>();
                var logger = sp.GetRequiredService<ILogger<SessionManager>>();
                return new SessionManager(client, logger);
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable("NEXUS_TEST_MODE", null);
    }
}
