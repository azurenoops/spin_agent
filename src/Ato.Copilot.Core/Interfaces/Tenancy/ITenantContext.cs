using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Per-request resolved tenant scope. Lifetime: <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped"/>.
/// Populated by <c>TenantResolutionMiddleware</c> after authentication and
/// before authorization. Consumed by EF query filters, the SaveChanges
/// interceptor, the SQL Server connection interceptor, every dashboard
/// endpoint, and every MCP tool.
/// See feature 048 spec FR-010..FR-024 and contracts/itenantcontext.cs.md.
/// </summary>
public interface ITenantContext
{
    /// <summary>The user's home tenant.</summary>
    /// <remarks>
    /// Resolved in this order:
    /// <list type="number">
    ///   <item>Entra <c>tid</c> claim → <c>Tenants.EntraTenantId</c> lookup.</item>
    ///   <item><c>X-Tenant-Id</c> header (only honored in dev/simulation mode).</item>
    ///   <item>Singleton default tenant in <c>SingleTenant</c> deployment mode.</item>
    /// </list>
    /// Throws <see cref="MissingTenantClaimException"/> if none resolve.
    /// </remarks>
    Guid TenantId { get; }

    /// <summary>Optional sub-organization scope. Null = tenant-level.</summary>
    Guid? OrganizationId { get; }

    /// <summary>True when the principal carries the <c>CSP.Admin</c> role.</summary>
    bool IsCspAdmin { get; }

    /// <summary>The tenant the principal is currently impersonating, if any.</summary>
    Guid? ImpersonatedTenantId { get; }

    /// <summary>
    /// The value used by query filters and stamping:
    /// <c>ImpersonatedTenantId ?? TenantId</c>.
    /// </summary>
    Guid EffectiveTenantId { get; }

    /// <summary>
    /// Lifecycle status of the effective tenant. Cached for 30 seconds in
    /// <c>IMemoryCache</c> by <c>TenantResolutionMiddleware</c> so downstream
    /// code does not re-query the DB on every read.
    /// </summary>
    TenantStatus Status { get; }
}
