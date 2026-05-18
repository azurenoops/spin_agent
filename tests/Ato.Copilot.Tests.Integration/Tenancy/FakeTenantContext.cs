using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// Mutable, thread-safe-by-immutability-of-call-pattern test double for
/// <see cref="ITenantContext"/>. Use the <see cref="SetTenant(Guid, bool, Guid?)"/>
/// helper to switch the resolved tenant per test case (or use the property
/// setters directly).
/// </summary>
/// <remarks>
/// Tests should mutate this object exactly once per request boundary. The
/// fixture lives in the integration-test DI container as a Scoped registration
/// (so each in-process request gets its own instance) when wired via
/// <see cref="MultiTenantWebApplicationFactory{TStartup}"/>.
/// </remarks>
public sealed class FakeTenantContext : ITenantContext
{
    public Guid TenantId { get; set; } = Guid.NewGuid();

    public Guid? OrganizationId { get; set; }

    public bool IsCspAdmin { get; set; }

    public Guid? ImpersonatedTenantId { get; set; }

    public Guid EffectiveTenantId => ImpersonatedTenantId ?? TenantId;

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>
    /// Convenience helper to set all switchable fields in one call.
    /// </summary>
    public void SetTenant(Guid tenantId, bool isCspAdmin = false, Guid? impersonatedTenantId = null)
    {
        TenantId = tenantId;
        IsCspAdmin = isCspAdmin;
        ImpersonatedTenantId = impersonatedTenantId;
    }
}
