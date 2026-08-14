namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Thrown by <see cref="Services.ResponseCacheService"/> when no tenant context
/// is available and the caller must fail-closed (hard fail per #790 posture).
/// Callers should surface this as HTTP 400 / TENANT_UNRESOLVED.
/// </summary>
public sealed class TenantUnresolvedException : Exception
{
    public TenantUnresolvedException()
        : base("No tenant context is resolved; request cannot proceed.") { }

    public TenantUnresolvedException(string message)
        : base(message) { }
}
