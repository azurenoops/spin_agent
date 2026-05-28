namespace Ato.Copilot.Core.Models.Auth;

/// <summary>
/// Distinct categories of authentication-related events emitted to the
/// <c>LoginAuditEvents</c> table per Feature 051 FR-032. Persisted as
/// the enum name via EF Core <c>HasConversion&lt;string&gt;()</c> so
/// raw-SQL audit queries are human-readable.
/// </summary>
public enum LoginAuditEventType
{
    /// <summary>User successfully authenticated.</summary>
    LoginSuccess = 0,
    /// <summary>Authentication attempt failed; see <c>ErrorClass</c>.</summary>
    LoginFailure = 1,
    /// <summary>User explicitly signed out.</summary>
    SignOut = 2,
    /// <summary>Server signed user out due to FR-007 idle timeout.</summary>
    IdleSignOut = 3,
    /// <summary>CSP-Admin started impersonating a customer tenant.</summary>
    ImpersonationStart = 4,
    /// <summary>Impersonation ended (manual exit, expiry, or idle).</summary>
    ImpersonationEnd = 5,
    /// <summary>User switched effective tenant via <c>/api/auth/select-tenant</c>.</summary>
    TenantSwitch = 6,
    /// <summary>Development-mode simulated login (FR-024).</summary>
    SimulatedLogin = 7,
    /// <summary>Simulated-login attempt blocked because environment != Development (FR-024).</summary>
    SimulationBlocked = 8,
}
