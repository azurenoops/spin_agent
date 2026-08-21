namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Provides the identity of the currently authenticated user for audit trail fields.
///
/// fix(DEF-002): replaces the hardcoded "dashboard-user" fallback that appeared in
/// 104 places across the dashboard endpoint layer.  Implementations must resolve the
/// principal from the active HTTP context and return a stable, non-empty identifier.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// The identifier of the currently authenticated user.
    ///
    /// Resolution order:
    ///   1. <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/> (UPN / sub)
    ///   2. <see cref="System.Security.Claims.ClaimTypes.Name"/> (display name)
    ///   3. <c>"unknown"</c> — never <c>"dashboard-user"</c>
    /// </summary>
    string CurrentUserId { get; }
}
