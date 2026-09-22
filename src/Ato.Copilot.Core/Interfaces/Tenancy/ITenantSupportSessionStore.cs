using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>Uncached, durable support-session state shared by all application instances.</summary>
public interface ITenantSupportSessionStore
{
    Task CreateAsync(TenantSupportSession session, CancellationToken cancellationToken);
    Task<TenantSupportSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<bool> RevokeAsync(Guid sessionId, Guid directoryTenantId, Guid objectId, Guid targetTenantId,
        string reason, CancellationToken cancellationToken);
}
