using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// CRUD-with-status surface for the <c>Tenants</c> table. Used by the
/// <c>/api/tenants</c> endpoint group (CSP-Admin only) and by the
/// onboarding wizard for self-onboarding paths. See feature 048 spec
/// FR-053..FR-059 and contracts/tenants.openapi.yaml.
/// </summary>
public interface ITenantProvisioningService
{
    /// <summary>
    /// Create a tenant or return the existing row when the supplied
    /// <paramref name="entraTenantId"/> already maps to one. Returned tuple's
    /// <c>created</c> flag indicates whether a new row was inserted.
    /// </summary>
    /// <param name="entraTenantId">Entra <c>tid</c> claim of the new tenant.</param>
    /// <param name="displayName">Human-readable name (1..200 chars).</param>
    /// <param name="actor">OID / username of the provisioning principal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The (possibly pre-existing) tenant row + <c>created</c> flag.</returns>
    Task<(Tenant Tenant, bool Created)> CreateAsync(
        Guid entraTenantId,
        string displayName,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Look up by id; returns <c>null</c> if no row exists.</summary>
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Look up by Entra <c>tid</c>; returns <c>null</c> if no row exists.</summary>
    Task<Tenant?> GetByEntraTenantIdAsync(Guid entraTenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated tenant list, filtered by lifecycle status when provided.
    /// </summary>
    Task<(IReadOnlyList<Tenant> Items, int Total)> ListAsync(
        TenantStatus? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Change a tenant's lifecycle status. Emits an audit row carrying
    /// <paramref name="actor"/> + <paramref name="reason"/>. Idempotent.
    /// Per FR-059.
    /// </summary>
    /// <returns>The updated tenant row.</returns>
    Task<Tenant> UpdateStatusAsync(
        Guid id,
        TenantStatus status,
        string reason,
        string actor,
        CancellationToken cancellationToken = default);
}
