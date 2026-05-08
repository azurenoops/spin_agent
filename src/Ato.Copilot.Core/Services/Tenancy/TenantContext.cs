using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Plain-data scoped implementation of <see cref="ITenantContext"/>.
/// Populated by <c>TenantResolutionMiddleware</c> in the MCP host or by
/// <c>ITenantContextAccessor.Push</c> in background / channel scenarios.
/// See feature 048 spec FR-010..FR-024 and data-model.md §3.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    /// <summary>
    /// Default constructor required by DI when the middleware later overwrites
    /// the instance via property setters or by registering a replacement.
    /// </summary>
    public TenantContext()
    {
    }

    /// <summary>Convenience constructor for tests and Push() calls.</summary>
    public TenantContext(
        Guid tenantId,
        Guid? organizationId = null,
        bool isCspAdmin = false,
        Guid? impersonatedTenantId = null,
        TenantStatus status = TenantStatus.Active)
    {
        TenantId = tenantId;
        OrganizationId = organizationId;
        IsCspAdmin = isCspAdmin;
        ImpersonatedTenantId = impersonatedTenantId;
        Status = status;
    }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public Guid? OrganizationId { get; set; }

    /// <inheritdoc />
    public bool IsCspAdmin { get; set; }

    /// <inheritdoc />
    public Guid? ImpersonatedTenantId { get; set; }

    /// <inheritdoc />
    public Guid EffectiveTenantId => ImpersonatedTenantId ?? TenantId;

    /// <inheritdoc />
    public TenantStatus Status { get; set; } = TenantStatus.Active;
}
