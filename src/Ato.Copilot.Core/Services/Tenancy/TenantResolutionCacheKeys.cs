namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Shared cache-key conventions for tenant resolution and onboarding-state
/// reads (Feature 048 / FR-054 / FR-058).
/// </summary>
/// <remarks>
/// <para>
/// The MCP-host <c>TenantResolutionMiddleware</c> caches per-tenant
/// <c>TenantStatus</c> and <c>OnboardingState</c> in <see cref="System.Object"/>
/// via <c>IMemoryCache</c> with a ~30 s TTL so the gate stays cheap on hot
/// paths. Core services that mutate either value (the onboarding wizard, the
/// tenant lifecycle service, etc.) MUST invalidate the matching key after
/// committing the write — otherwise the middleware will continue serving
/// stale state for up to the TTL and 403 every non-allowlisted request with
/// <c>TENANT_ONBOARDING_INCOMPLETE</c> or <c>TENANT_SUSPENDED</c>.
/// </para>
/// <para>
/// This class is the single source of truth for the key strings so the
/// middleware and the producing services cannot drift.
/// </para>
/// </remarks>
public static class TenantResolutionCacheKeys
{
    /// <summary>Prefix for cached <c>TenantStatus</c> reads.</summary>
    public const string TenantStatusPrefix = "tenant-status:";

    /// <summary>Prefix for cached <c>OnboardingState</c> reads.</summary>
    public const string TenantOnboardingPrefix = "tenant-onboarding:";

    /// <summary>Builds the <c>TenantStatus</c> cache key for a tenant.</summary>
    public static string TenantStatus(System.Guid tenantId) =>
        TenantStatusPrefix + tenantId;

    /// <summary>Builds the <c>OnboardingState</c> cache key for a tenant.</summary>
    public static string TenantOnboarding(System.Guid tenantId) =>
        TenantOnboardingPrefix + tenantId;
}
