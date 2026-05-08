namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Lifecycle status of a <see cref="Tenant"/>.
/// See feature 048 spec FR-057..FR-059.
/// </summary>
public enum TenantStatus
{
    /// <summary>Normal operation. All requests are honored.</summary>
    Active = 0,

    /// <summary>
    /// Read-only. Mutating endpoints return <c>423 TENANT_SUSPENDED</c>.
    /// Reversible by a CSP-Admin via <c>PATCH /api/tenants/{id}/status</c>.
    /// </summary>
    Suspended = 1,

    /// <summary>
    /// All requests fail with <c>401 TENANT_DISABLED</c>. Reversible.
    /// </summary>
    Disabled = 2
}
