namespace CopilotNexus.Service.Tests;

using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// WebApplicationFactory that swaps the real SDK for mock services.
/// Sets NEXUS_TEST_MODE to prevent lock file contention during parallel tests.
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
            // Remove registered runtime adapters.
            var adapterDescriptors = services
                .Where(descriptor => descriptor.ServiceType == typeof(IAgentClientService))
                .ToList();
            foreach (var descriptor in adapterDescriptors)
                services.Remove(descriptor);

            // Remove real session manager
            var managerDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISessionManager));
            if (managerDescriptor != null) services.Remove(managerDescriptor);

            // Register mock runtime adapter.
            services.AddSingleton<IAgentClientService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<MockCopilotClientService>>();
                return new MockCopilotClientService(logger);
            });

            // Register session manager with mock runtime
            services.AddSingleton<ISessionManager>(sp =>
            {
                var client = sp.GetRequiredService<IAgentClientService>();
                var logger = sp.GetRequiredService<ILogger<SessionManager>>();
                return new SessionManager(client, logger);
            });
        });
    }
}
