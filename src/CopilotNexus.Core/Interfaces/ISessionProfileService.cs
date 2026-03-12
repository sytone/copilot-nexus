namespace CopilotNexus.Core.Interfaces;

using CopilotNexus.Core.Models;

/// <summary>
/// CRUD service for session profiles.
/// Implemented by Nexus (authoritative) and consumed by all clients.
/// </summary>
public interface ISessionProfileService
{
    Task<IReadOnlyList<SessionProfile>> ListAsync(CancellationToken cancellationToken = default);
    Task<SessionProfile?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<SessionProfile> SaveAsync(SessionProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
